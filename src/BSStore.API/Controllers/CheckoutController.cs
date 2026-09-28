using System.Security.Claims;
using System.Text.Json;
using BSStore.Application.Common;
using BSStore.Application.Orders.DTOs;
using BSStore.Domain.Entities;
using BSStore.Domain.Enums;
using BSStore.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BSStore.API.Controllers;

[ApiController]
[Route("api/checkout")]
[Authorize]
[Produces("application/json")]
public class CheckoutController : ControllerBase
{
    private readonly AppDbContext _db;

    public CheckoutController(AppDbContext db)
    {
        _db = db;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue("userId")!);

    [HttpPost("delivery-quote")]
    public async Task<IActionResult> GetDeliveryQuote([FromBody] DeliveryQuoteRequest request, CancellationToken ct)
    {
        var customer = await _db.Customers.Include(c => c.Addresses).FirstOrDefaultAsync(c => c.UserId == GetUserId(), ct);
        var address = customer?.Addresses.FirstOrDefault(a => request.AddressId.HasValue && a.Id == request.AddressId.Value)
            ?? customer?.Addresses.FirstOrDefault(a => a.IsDefault)
            ?? customer?.Addresses.FirstOrDefault();
        if (address == null) return BadRequest(ApiResponse.Fail("يرجى تحديد عنوان التوصيل"));

        var storeIds = request.StoreIds ?? [];
        var stores = storeIds.Distinct().Count() == 0
            ? new List<Store>()
            : await _db.Stores.Where(s => storeIds.Contains(s.Id)).ToListAsync(ct);
        var settings = await _db.DeliverySettings.FirstOrDefaultAsync(ct)
            ?? new DeliverySettings { BaseFee = 400, PerKmFee = 50, BaseDistanceKm = 1.0m, IsEnabled = true };
        var quote = CalculateGroupDeliveryFee(stores, address.Latitude, address.Longitude, settings);
        return Ok(ApiResponse<DeliveryQuoteResult>.Ok(new DeliveryQuoteResult(quote.Fee, quote.FarthestDistanceKm)));
    }

    /// <summary>Validate checkout items, prices, delivery fee and COD eligibility.</summary>
    [HttpPost("validate")]
    [ProducesResponseType(typeof(ApiResponse<CheckoutValidationResult>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ValidateCheckout([FromBody] ValidateCheckoutRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var errors = new List<string>();

        if (request.Items == null || request.Items.Count == 0)
        {
            errors.Add("قائمة الأصناف فارغة");
            return Ok(ApiResponse<CheckoutValidationResult>.Ok(new CheckoutValidationResult(false, errors, 0, 0, 0, 0, false, 0)));
        }

        var productIds = request.Items.Where(i => i.ProductId.HasValue).Select(i => i.ProductId!.Value).Distinct().ToList();
        var products = await _db.Products
            .Where(p => productIds.Contains(p.Id))
            .Include(p => p.Inventory)
            .ToDictionaryAsync(p => p.Id, ct);

        decimal subTotal = 0;

        foreach (var item in request.Items)
        {
            if (!item.ProductId.HasValue || !products.TryGetValue(item.ProductId.Value, out var product) || !product.IsActive)
            {
                errors.Add($"أحد المنتجات لم يعد متاحاً في المتجر");
                continue;
            }

            if (product.SourceType == SourceType.InStock)
            {
                var available = product.Inventory?.Quantity ?? 0;
                if (available < item.Quantity)
                {
                    errors.Add($"الكمية المطلوبة من '{product.Name}' غير متوفرة في المخزن (المتاح: {available})");
                }
            }

            subTotal += product.SellingPrice * item.Quantity;
        }

        var deliverySettings = await _db.DeliverySettings.FirstOrDefaultAsync(ct)
            ?? new DeliverySettings { BaseFee = 400, PerKmFee = 50, BaseDistanceKm = 1.0m, IsEnabled = true };

        var paymentSettings = await _db.PaymentSettings.FirstOrDefaultAsync(ct)
            ?? new PaymentSettings { CashOnDeliveryLimit = 50000 };

        Address? selectedAddress = null;
        if (request.AddressId.HasValue)
        {
            selectedAddress = await _db.Addresses.FirstOrDefaultAsync(a => a.Id == request.AddressId.Value, ct);
        }
        else
        {
            var customer = await _db.Customers.Include(c => c.Addresses).FirstOrDefaultAsync(c => c.UserId == userId, ct);
            selectedAddress = customer?.Addresses.FirstOrDefault(a => a.IsDefault) ?? customer?.Addresses.FirstOrDefault();
        }

        decimal deliveryFee = 0;
        double totalDistanceKm = 0;

        if (deliverySettings.IsEnabled && request.Items.Count > 0 && selectedAddress != null)
        {
            var storeIds = request.Items
                .Where(i => i.ProductId.HasValue && products.ContainsKey(i.ProductId.Value))
                .Select(i => products[i.ProductId!.Value].StoreId)
                .Distinct()
                .ToList();

            var stores = await _db.Stores.Where(s => storeIds.Contains(s.Id)).ToListAsync(ct);

            var deliveryQuote = CalculateGroupDeliveryFee(stores, selectedAddress.Latitude, selectedAddress.Longitude, deliverySettings);
            deliveryFee = deliveryQuote.Fee;
            totalDistanceKm = deliveryQuote.FarthestDistanceKm;
        }

        var discount = 0m;
        var totalAmount = subTotal + deliveryFee - discount;

        var codAllowed = totalAmount <= paymentSettings.CashOnDeliveryLimit;
        if (request.PaymentMethod == PaymentMethod.CashOnDelivery && !codAllowed)
        {
            errors.Add($"الدفع عند الاستلام متاح فقط للطلبات الأقل من أو تساوي {paymentSettings.CashOnDeliveryLimit:N0} ريال. يرجى اختيار التحويل البنكي.");
        }

        var result = new CheckoutValidationResult(
            IsValid: errors.Count == 0,
            Errors: errors,
            SubTotal: subTotal,
            DeliveryFee: deliveryFee,
            Discount: discount,
            TotalAmount: totalAmount,
            CodAllowed: codAllowed,
            CodLimit: paymentSettings.CashOnDeliveryLimit,
            EstimatedDistanceKm: totalDistanceKm
        );

        return Ok(ApiResponse<CheckoutValidationResult>.Ok(result));
    }

    /// <summary>Place order atomically from customer cart with snapshot prices and store grouping.</summary>
    [HttpPost("place-order")]
    [ProducesResponseType(typeof(ApiResponse<OrderDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var customer = await _db.Customers
            .Include(c => c.User)
            .Include(c => c.Addresses)
            .Include(c => c.Cart)
            .ThenInclude(cart => cart!.Items)
            .ThenInclude(item => item.Product)
            .ThenInclude(p => p.Store)
            .Include(c => c.Cart)
            .ThenInclude(cart => cart!.Items)
            .ThenInclude(item => item.Product)
            .ThenInclude(p => p.Inventory)
            .FirstOrDefaultAsync(c => c.UserId == userId, ct);

        if (customer == null)
            return Unauthorized(ApiResponse.Fail("حساب العميل غير موجود"));

        // Idempotency check
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existingOrder = await _db.Orders
                .Include(o => o.Customer)
                    .ThenInclude(c => c.User)
                .Include(o => o.SubOrders)
                .ThenInclude(so => so.Items)
                .Include(o => o.SubOrders)
                .ThenInclude(so => so.Store)
                .Include(o => o.Address)
                .FirstOrDefaultAsync(o => o.IdempotencyKey == request.IdempotencyKey, ct);

            if (existingOrder != null)
            {
                return Ok(ApiResponse<OrderDto>.Ok(MapToDto(existingOrder), "تم استرجاع الطلب المسجل مسبقاً"));
            }
        }

        List<CartItem> cartItems = [];
        var catalogProductIds = new HashSet<Guid>();
        if (request.Items != null && request.Items.Count > 0)
        {
            var productIds = request.Items.Where(i => i.ProductId.HasValue).Select(i => i.ProductId!.Value).Distinct().ToList();
            var products = await _db.Products
                .Where(p => productIds.Contains(p.Id))
                .Include(p => p.Store)
                .Include(p => p.Inventory)
                .ToDictionaryAsync(p => p.Id, ct);
            catalogProductIds.UnionWith(products.Keys);

            foreach (var input in request.Items)
            {
                if (input.ProductId.HasValue && products.TryGetValue(input.ProductId.Value, out var product))
                {
                    cartItems.Add(new CartItem
                    {
                        ProductId = product.Id,
                        Product = product,
                        Quantity = input.Quantity
                    });
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(input.DealId))
                {
                    var dealSetting = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == "FlashDeals", ct);
                    if (dealSetting != null && !string.IsNullOrWhiteSpace(dealSetting.Value))
                    {
                        try
                        {
                            using var dealDoc = System.Text.Json.JsonDocument.Parse(dealSetting.Value);
                            var deal = dealDoc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array
                                ? dealDoc.RootElement.EnumerateArray().FirstOrDefault(d =>
                                    d.TryGetProperty("id", out var id) && id.GetString() == input.DealId &&
                                    d.TryGetProperty("dealKind", out var kind) && kind.GetString() == "BUNDLE" &&
                                    (!d.TryGetProperty("isActive", out var active) || active.ValueKind != System.Text.Json.JsonValueKind.False) &&
                                    (!d.TryGetProperty("isPublic", out var visible) || visible.ValueKind != System.Text.Json.JsonValueKind.False) &&
                                    d.TryGetProperty("expiresAt", out var expires) && DateTimeOffset.TryParse(expires.GetString(), out var expiry) && expiry > DateTimeOffset.UtcNow)
                                : default;

                            if (deal.ValueKind == System.Text.Json.JsonValueKind.Object &&
                                deal.TryGetProperty("storeId", out var storeIdJson) && Guid.TryParse(storeIdJson.GetString(), out var storeId))
                            {
                                var store = await _db.Stores.FirstOrDefaultAsync(s => s.Id == storeId && s.IsActive, ct);
                                if (store != null)
                                {
                                    string name;
                                    string description = string.Empty;
                                    string imageUrl = string.Empty;
                                    decimal price;
                                    if (string.IsNullOrWhiteSpace(input.DealProductId))
                                    {
                                        name = deal.TryGetProperty("title", out var title) ? title.GetString() ?? "باقة منتجات" : "باقة منتجات";
                                        price = ReadDecimal(deal, "discountPrice", ReadDecimal(deal, "offerPrice", 0));
                                    }
                                    else if (deal.TryGetProperty("products", out var pieces) && pieces.ValueKind == System.Text.Json.JsonValueKind.Array)
                                    {
                                        var piece = pieces.EnumerateArray().FirstOrDefault(p => p.TryGetProperty("id", out var pieceId) && pieceId.GetString() == input.DealProductId);
                                        if (piece.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                                        name = piece.TryGetProperty("name", out var pieceName) ? pieceName.GetString() ?? "منتج الباقة" : "منتج الباقة";
                                        description = piece.TryGetProperty("description", out var pieceDesc) ? pieceDesc.GetString() ?? string.Empty : string.Empty;
                                        imageUrl = piece.TryGetProperty("imageUrl", out var pieceImage) ? pieceImage.GetString() ?? string.Empty : string.Empty;
                                        price = ReadDecimal(piece, "price", 0);
                                    }
                                    else continue;

                                    var dealProduct = new Product
                                    {
                                        Id = Guid.NewGuid(), Name = name, Description = description,
                                        StoreId = store.Id, Store = store, CategoryId = Guid.Empty,
                                        SourceType = SourceType.ExternalStore, SellingPrice = price,
                                        ExpectedPurchasePrice = price, IsActive = true
                                    };
                                    cartItems.Add(new CartItem { ProductId = dealProduct.Id, Product = dealProduct, Quantity = input.Quantity });
                                }
                            }
                        }
                        catch (System.Text.Json.JsonException) { }
                    }
                }
            }
        }
        else
        {
            cartItems = customer.Cart?.Items.ToList() ?? [];
        }

        if (cartItems.Count == 0)
            return BadRequest(ApiResponse.Fail("سلة التسوق فارغة"));

        // Address resolution
        Address? address = null;
        if (request.AddressId.HasValue)
        {
            address = customer.Addresses.FirstOrDefault(a => a.Id == request.AddressId.Value);
        }
        else if (request.NewAddress != null)
        {
            address = new Address
            {
                CustomerId = customer.Id,
                Label = request.NewAddress.Label,
                City = request.NewAddress.City,
                District = request.NewAddress.District,
                Street = request.NewAddress.Street,
                Building = request.NewAddress.Building,
                Description = request.NewAddress.Description,
                Latitude = request.NewAddress.Latitude,
                Longitude = request.NewAddress.Longitude,
                IsDefault = !customer.Addresses.Any()
            };
            _db.Addresses.Add(address);
        }

        if (address == null)
            return BadRequest(ApiResponse.Fail("يرجى تحديد عنوان التوصيل"));

        var deliverySettings = await _db.DeliverySettings.FirstOrDefaultAsync(ct)
            ?? new DeliverySettings { BaseFee = 500, IsEnabled = true };

        var paymentSettings = await _db.PaymentSettings.FirstOrDefaultAsync(ct)
            ?? new PaymentSettings { CashOnDeliveryLimit = 50000 };

        // Begin DB Transaction for atomic order creation and inventory reservation
        using var tx = await _db.Database.BeginTransactionAsync(ct);

        decimal subTotal = 0;
        var subOrdersList = new List<SubOrder>();

        // Group cart items by store
        var storeGroups = cartItems.GroupBy(i => i.Product.StoreId);

        foreach (var group in storeGroups)
        {
            var storeId = group.Key;
            decimal storeSubTotal = 0;
            decimal storeExpectedCost = 0;
            var orderItemsList = new List<OrderItem>();

            foreach (var ci in group)
            {
                var product = ci.Product;
                if (!product.IsActive)
                {
                    await tx.RollbackAsync(ct);
                    return BadRequest(ApiResponse.Fail($"المنتج '{product.Name}' غير متاح حالياً"));
                }

                // Inventory check and reservation for IN_STOCK
                if (product.SourceType == SourceType.InStock)
                {
                    if (product.Inventory == null || product.Inventory.Quantity < ci.Quantity)
                    {
                        await tx.RollbackAsync(ct);
                        return BadRequest(ApiResponse.Fail($"الكمية المطلوبة من '{product.Name}' غير متوفرة"));
                    }

                    // Decrement stock & record reservation
                    product.Inventory.Quantity -= ci.Quantity;
                    _db.InventoryTransactions.Add(new InventoryTransaction
                    {
                        ProductId = product.Id,
                        Type = InventoryTransactionType.Reservation,
                        Quantity = ci.Quantity,
                        Note = $"حجز كمية للطلب الجديد"
                    });
                }

                var itemTotal = product.SellingPrice * ci.Quantity;
                var itemCost = product.ExpectedPurchasePrice * ci.Quantity;
                var itemProfit = itemTotal - itemCost;

                var orderItem = new OrderItem
                {
                    ProductId = catalogProductIds.Contains(product.Id) ? product.Id : null,
                    ProductNameSnapshot = product.Name,
                    SellingPriceSnapshot = product.SellingPrice,
                    ExpectedPurchasePriceSnapshot = product.ExpectedPurchasePrice,
                    ExpectedProfitSnapshot = itemProfit,
                    Quantity = ci.Quantity,
                    TotalSellingPrice = itemTotal
                };

                orderItemsList.Add(orderItem);
                storeSubTotal += itemTotal;
                storeExpectedCost += itemCost;
            }

            var subOrder = new SubOrder
            {
                StoreId = storeId,
                Status = OrderStatus.Confirmed,
                ExpectedPurchaseCost = storeExpectedCost,
                SubTotal = storeSubTotal,
                Items = orderItemsList
            };

            subOrdersList.Add(subOrder);
            subTotal += storeSubTotal;
        }

        decimal deliveryFee = 0;
        if (deliverySettings.IsEnabled && subOrdersList.Count > 0)
        {
            var distinctStoreIds = subOrdersList.Select(so => so.StoreId).Distinct().ToList();
            var stores = await _db.Stores.Where(s => distinctStoreIds.Contains(s.Id)).ToListAsync(ct);

            deliveryFee = CalculateGroupDeliveryFee(stores, address.Latitude, address.Longitude, deliverySettings).Fee;
        }

        if (string.IsNullOrWhiteSpace(request.CouponCode) && !string.IsNullOrWhiteSpace(request.DeliveryDealId))
        {
            var dealsSetting = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == "FlashDeals", ct);
            if (dealsSetting != null && !string.IsNullOrWhiteSpace(dealsSetting.Value))
            {
                try
                {
                    using var dealsDoc = System.Text.Json.JsonDocument.Parse(dealsSetting.Value);
                    if (dealsDoc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        var deliveryDeal = dealsDoc.RootElement.EnumerateArray().FirstOrDefault(d =>
                            d.TryGetProperty("id", out var id) && id.GetString() == request.DeliveryDealId &&
                            d.TryGetProperty("dealKind", out var kind) && kind.GetString() == "DELIVERY_DISCOUNT" &&
                            (!d.TryGetProperty("dealType", out var type) || type.GetString() == "DELIVERY_DISCOUNT") &&
                            (!d.TryGetProperty("isActive", out var active) || active.ValueKind != System.Text.Json.JsonValueKind.False) &&
                            (!d.TryGetProperty("isPublic", out var visible) || visible.ValueKind != System.Text.Json.JsonValueKind.False) &&
                            d.TryGetProperty("expiresAt", out var expires) && DateTimeOffset.TryParse(expires.GetString(), out var expiry) && expiry > DateTimeOffset.UtcNow);
                        if (deliveryDeal.ValueKind == System.Text.Json.JsonValueKind.Object &&
                            deliveryDeal.TryGetProperty("deliveryDiscountPercent", out var percentElement) &&
                            percentElement.TryGetInt32(out var percent) && percent is >= 1 and <= 100)
                        {
                            deliveryFee = Math.Round(deliveryFee * (100 - percent) / 100m, 0, MidpointRounding.AwayFromZero);
                        }
                    }
                }
                catch (System.Text.Json.JsonException) { }
            }
        }

        var discount = 0m;
        if (!string.IsNullOrWhiteSpace(request.CouponCode))
        {
            var couponSetting = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == "FlashDeals", ct);
            JsonElement coupon = default;
            if (couponSetting != null && !string.IsNullOrWhiteSpace(couponSetting.Value))
            {
                try
                {
                    using var couponDoc = System.Text.Json.JsonDocument.Parse(couponSetting.Value);
                    if (couponDoc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        coupon = couponDoc.RootElement.EnumerateArray().FirstOrDefault(item =>
                            item.TryGetProperty("couponCode", out var storedCode) &&
                            string.Equals(storedCode.GetString()?.Trim(), request.CouponCode.Trim(), StringComparison.OrdinalIgnoreCase) &&
                            (!item.TryGetProperty("isActive", out var active) || active.ValueKind != System.Text.Json.JsonValueKind.False) &&
                            item.TryGetProperty("expiresAt", out var expires) &&
                            DateTimeOffset.TryParse(expires.GetString(), out var expiry) && expiry > DateTimeOffset.UtcNow);
                    }
                }
                catch (System.Text.Json.JsonException) { }
            }

            if (coupon.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                await tx.RollbackAsync(ct);
                return BadRequest(ApiResponse.Fail("كود الخصم غير صالح أو منتهي"));
            }

            var couponKind = coupon.TryGetProperty("discountKind", out var couponKindElement) ? couponKindElement.GetString() : null;
            if (couponKind is not ("DELIVERY_DISCOUNT" or "PRODUCT_DISCOUNT") && coupon.TryGetProperty("dealType", out var couponTypeElement))
                couponKind = couponTypeElement.GetString();
            var couponPercent = couponKind == "DELIVERY_DISCOUNT" && coupon.TryGetProperty("deliveryDiscountPercent", out var deliveryPercentElement) && deliveryPercentElement.TryGetDecimal(out var parsedDeliveryPercent)
                ? parsedDeliveryPercent
                : coupon.TryGetProperty("discountPercent", out var couponPercentElement) && couponPercentElement.TryGetDecimal(out var parsedCouponPercent) ? parsedCouponPercent : 0m;
            if (couponKind is not ("DELIVERY_DISCOUNT" or "PRODUCT_DISCOUNT") || couponPercent is <= 0 or > 100)
            {
                await tx.RollbackAsync(ct);
                return BadRequest(ApiResponse.Fail("كود الخصم غير صالح"));
            }

            if (couponKind == "DELIVERY_DISCOUNT")
                deliveryFee = Math.Round(deliveryFee * (100m - couponPercent) / 100m, 0, MidpointRounding.AwayFromZero);
            else
                discount = Math.Round(subTotal * couponPercent / 100m, 0, MidpointRounding.AwayFromZero);
        }
        var totalAmount = subTotal + deliveryFee - discount;

        if (request.PaymentMethod == PaymentMethod.CashOnDelivery && totalAmount > paymentSettings.CashOnDeliveryLimit)
        {
            await tx.RollbackAsync(ct);
            return BadRequest(ApiResponse.Fail($"تجاوزت قيمة الطلب الحد المسموح للدفع عند الاستلام ({paymentSettings.CashOnDeliveryLimit:N0} ريال)"));
        }

        // Determine Initial Order and Payment Status
        var initialOrderStatus = request.PaymentMethod == PaymentMethod.CashOnDelivery
            ? OrderStatus.Confirmed
            : OrderStatus.PendingPayment;

        var initialPaymentStatus = PaymentStatus.Pending;

        var orderNumber = $"BS-{DateTime.UtcNow:yyyyMMdd}-{new Random().Next(1000, 9999)}";

        var order = new Order
        {
            OrderNumber = orderNumber,
            CustomerId = customer.Id,
            Address = address,
            Status = initialOrderStatus,
            PaymentMethod = request.PaymentMethod,
            PaymentStatus = initialPaymentStatus,
            SubTotal = subTotal,
            DeliveryFee = deliveryFee,
            Discount = discount,
            TotalAmount = totalAmount,
            Notes = request.Notes,
            IdempotencyKey = request.IdempotencyKey,
            SubOrders = subOrdersList
        };

        // Create Payment record
        var payment = new Payment
        {
            Order = order,
            Method = request.PaymentMethod,
            Status = initialPaymentStatus,
            Amount = totalAmount
        };
        _db.Payments.Add(payment);

        // Add Status History
        order.StatusHistory.Add(new OrderStatusHistory
        {
            OldStatus = OrderStatus.PendingPayment,
            NewStatus = initialOrderStatus,
            ActorType = "Customer",
            ActorId = userId,
            Reason = "إنشاء الطلب"
        });

        // Clear Cart items if any
        if (customer.Cart?.Items != null && customer.Cart.Items.Count > 0)
        {
            _db.CartItems.RemoveRange(customer.Cart.Items);
        }

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // Load store names and customer user for clean DTO
        await _db.Entry(order).Collection(o => o.SubOrders).Query().Include(so => so.Store).LoadAsync(ct);
        await _db.Entry(order).Reference(o => o.Customer).Query().Include(c => c.User).LoadAsync(ct);

        return Ok(ApiResponse<OrderDto>.Ok(MapToDto(order), "تم إنشاء طلبك بنجاح!"));
    }

    private static OrderDto MapToDto(Order o)
    {
        var addressText = o.Address != null
            ? $"{o.Address.City}, {o.Address.District}, {o.Address.Street}"
            : "العنوان المحدد";

        return new OrderDto(
            o.Id,
            o.OrderNumber,
            o.Status,
            o.PaymentMethod,
            o.PaymentStatus,
            o.SubTotal,
            o.DeliveryFee,
            o.Discount,
            o.TotalAmount,
            o.Notes,
            o.CreatedAt,
            o.Driver?.FullName,
            o.Driver?.Phone,
            addressText,
            o.SubOrders.Select(so => new SubOrderDto(
                so.Id,
                so.StoreId,
                so.Store?.Name ?? "متجر",
                so.Status,
                so.SubTotal,
                so.Items.Select(i => new OrderItemDto(
                    i.Id,
                    i.ProductId,
                    i.ProductNameSnapshot,
                    i.SellingPriceSnapshot,
                    i.Quantity,
                    i.TotalSellingPrice,
                    i.ActualPurchasePrice
                )).ToList()
            )).ToList(),
            o.Customer?.FullName ?? "عميل المنصة",
            o.Customer?.User?.Phone ?? ""
        );
    }

    private static double CalculateDistanceKm(decimal lat1, decimal lon1, decimal lat2, decimal lon2)
    {
        const double EarthRadiusKm = 6371.0;
        var dLat = DegreesToRadians((double)(lat2 - lat1));
        var dLon = DegreesToRadians((double)(lon2 - lon1));

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(DegreesToRadians((double)lat1)) * Math.Cos(DegreesToRadians((double)lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return Math.Round(EarthRadiusKm * c, 2);
    }

    private static decimal ReadDecimal(System.Text.Json.JsonElement element, string property, decimal fallback)
    {
        if (!element.TryGetProperty(property, out var value)) return fallback;
        if (value.ValueKind == System.Text.Json.JsonValueKind.Number && value.TryGetDecimal(out var number)) return number;
        if (value.ValueKind == System.Text.Json.JsonValueKind.String && decimal.TryParse(value.GetString(), out number)) return number;
        return fallback;
    }

    private static double DegreesToRadians(double deg) => deg * (Math.PI / 180.0);

    private static decimal CalculateDeliveryFee(double distanceKm, DeliverySettings settings)
    {
        if (!settings.IsEnabled) return 0;
        var extraKm = Math.Max(0, distanceKm - (double)settings.BaseDistanceKm);
        var kmUnits = (decimal)Math.Ceiling(extraKm);
        return settings.BaseFee + (kmUnits * settings.PerKmFee);
    }

    private static (decimal Fee, double FarthestDistanceKm) CalculateGroupDeliveryFee(
        IReadOnlyCollection<Store> stores, decimal customerLatitude, decimal customerLongitude, DeliverySettings settings)
    {
        if (!settings.IsEnabled || stores.Count == 0) return (0, 0);
        var charges = stores.Select(store =>
        {
            var distance = CalculateDistanceKm(store.Latitude, store.Longitude, customerLatitude, customerLongitude);
            return (DistanceKm: distance, Fee: CalculateDeliveryFee(distance, settings));
        }).OrderByDescending(item => item.DistanceKm).ToList();

        if (charges.Count == 1) return (charges[0].Fee, charges[0].DistanceKm);

        var combined = charges[0].Fee + charges.Skip(1).Sum(item => item.Fee) / 2m;
        return (Math.Round(combined, 0, MidpointRounding.AwayFromZero), charges[0].DistanceKm);
    }
}
