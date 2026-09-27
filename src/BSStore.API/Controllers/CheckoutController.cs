using System.Security.Claims;
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

        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _db.Products
            .Where(p => productIds.Contains(p.Id))
            .Include(p => p.Inventory)
            .ToDictionaryAsync(p => p.Id, ct);

        decimal subTotal = 0;

        foreach (var item in request.Items)
        {
            if (!products.TryGetValue(item.ProductId, out var product) || !product.IsActive)
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

        if (deliverySettings.IsEnabled && request.Items.Count > 0)
        {
            var storeIds = request.Items
                .Where(i => products.ContainsKey(i.ProductId))
                .Select(i => products[i.ProductId].StoreId)
                .Distinct()
                .ToList();

            var stores = await _db.Stores.Where(s => storeIds.Contains(s.Id)).ToListAsync(ct);

            foreach (var store in stores)
            {
                var distance = selectedAddress != null
                    ? CalculateDistanceKm(store.Latitude, store.Longitude, selectedAddress.Latitude, selectedAddress.Longitude)
                    : 1.0;

                totalDistanceKm += distance;
                deliveryFee += CalculateDeliveryFee(distance, deliverySettings);
            }

            if (deliveryFee == 0 && stores.Count > 0)
                deliveryFee = deliverySettings.BaseFee;
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

        var cartItems = customer.Cart?.Items.ToList() ?? [];
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
                    ProductId = product.Id,
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

            foreach (var store in stores)
            {
                var distance = CalculateDistanceKm(store.Latitude, store.Longitude, address.Latitude, address.Longitude);
                deliveryFee += CalculateDeliveryFee(distance, deliverySettings);
            }

            if (deliveryFee == 0 && stores.Count > 0)
                deliveryFee = deliverySettings.BaseFee;
        }

        var discount = 0m;
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

        // Clear Cart items
        _db.CartItems.RemoveRange(customer.Cart!.Items);

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // Load store names for clean DTO
        await _db.Entry(order).Collection(o => o.SubOrders).Query().Include(so => so.Store).LoadAsync(ct);

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
            )).ToList()
        );
    }

    private static double CalculateDistanceKm(decimal lat1, decimal lon1, decimal lat2, decimal lon2)
    {
        if (lat1 == 0 || lon1 == 0 || lat2 == 0 || lon2 == 0)
            return 1.0;

        const double EarthRadiusKm = 6371.0;
        var dLat = DegreesToRadians((double)(lat2 - lat1));
        var dLon = DegreesToRadians((double)(lon2 - lon1));

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(DegreesToRadians((double)lat1)) * Math.Cos(DegreesToRadians((double)lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return Math.Round(EarthRadiusKm * c, 2);
    }

    private static double DegreesToRadians(double deg) => deg * (Math.PI / 180.0);

    private static decimal CalculateDeliveryFee(double distanceKm, DeliverySettings settings)
    {
        if (!settings.IsEnabled) return 0;
        var baseKm = (double)settings.BaseDistanceKm;
        if (distanceKm <= baseKm)
            return settings.BaseFee;

        // Formula: BaseFee + (Ceil(distanceKm) * PerKmFee)
        // e.g. 5 km -> 400 + (5 * 50) = 650
        var kmUnits = (decimal)Math.Ceiling(distanceKm);
        return settings.BaseFee + (kmUnits * settings.PerKmFee);
    }
}
