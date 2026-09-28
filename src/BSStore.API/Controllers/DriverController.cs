using System.Security.Claims;
using BSStore.Application.Common;
using BSStore.Application.Driver.DTOs;
using BSStore.Application.Orders.DTOs;
using BSStore.Domain.Entities;
using BSStore.Domain.Enums;
using BSStore.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BSStore.API.Controllers;

[ApiController]
[Route("api/driver")]
[Authorize(Roles = "Driver,Admin,SuperAdmin")]
[Produces("application/json")]
public class DriverController : ControllerBase
{
    private readonly AppDbContext _db;

    public DriverController(AppDbContext db)
    {
        _db = db;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue("userId")!);

    private async Task<Driver?> GetCurrentDriverAsync(CancellationToken ct)
    {
        var userId = GetUserId();
        return await _db.Drivers.FirstOrDefaultAsync(d => d.UserId == userId, ct);
    }

    /// <summary>Get current driver status and statistics.</summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(ApiResponse<DriverStatusDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        var driver = await GetCurrentDriverAsync(ct);
        if (driver == null) return NotFound(ApiResponse.Fail("ملف السائق غير موجود"));

        var today = DateTime.UtcNow.Date;
        var completedCount = await _db.Orders.CountAsync(o =>
            o.DriverId == driver.Id &&
            o.Status == OrderStatus.Delivered &&
            o.UpdatedAt >= today, ct);

        var dto = new DriverStatusDto(
            driver.Id,
            driver.FullName,
            driver.Phone,
            driver.Status,
            driver.Status == DriverStatus.Online || driver.Status == DriverStatus.Busy,
            completedCount
        );

        return Ok(ApiResponse<DriverStatusDto>.Ok(dto));
    }

    [HttpPost("financial/settlements")]
    public async Task<IActionResult> RequestSettlement([FromBody] DriverSettlementRequest request, CancellationToken ct)
    {
        if (request.Amount <= 0) return BadRequest(ApiResponse.Fail("أدخل مبلغاً صحيحاً"));
        var driver = await GetCurrentDriverAsync(ct);
        if (driver == null) return NotFound(ApiResponse.Fail("ملف السائق غير موجود"));
        var collections = await _db.DriverTransactions.Where(t => t.DriverId == driver.Id && t.Type == DriverTransactionType.CustomerCollection).SumAsync(t => (decimal?)t.Amount, ct) ?? 0;
        var purchases = await _db.DriverTransactions.Where(t => t.DriverId == driver.Id && t.Type == DriverTransactionType.StorePurchase).SumAsync(t => (decimal?)t.Amount, ct) ?? 0;
        var settlements = await _db.DriverTransactions.Where(t => t.DriverId == driver.Id && t.Type == DriverTransactionType.CompanySettlement).SumAsync(t => (decimal?)t.Amount, ct) ?? 0;
        var fundings = await _db.DriverTransactions.Where(t => t.DriverId == driver.Id && t.Type == DriverTransactionType.DriverFunding).SumAsync(t => (decimal?)t.Amount, ct) ?? 0;
        var outstanding = collections - purchases - settlements + fundings;
        if (request.Amount > outstanding) return BadRequest(ApiResponse.Fail("المبلغ أكبر من العهدة المستحقة حالياً"));

        _db.DriverSettlements.Add(new DriverSettlement { DriverId = driver.Id, Amount = request.Amount, Note = request.Note, CreatedBy = driver.UserId });
        _db.DriverTransactions.Add(new DriverTransaction
        {
            DriverId = driver.Id,
            Type = DriverTransactionType.CompanySettlement,
            Amount = request.Amount,
            Direction = TransactionDirection.Credit,
            Reference = "طلب توريد",
            Description = request.Note ?? "طلب توريد عهدة للإدارة",
            CreatedBy = driver.UserId
        });
        await _db.SaveChangesAsync(ct);
        return Ok(ApiResponse.Ok("تم تسجيل طلب التوريد"));
    }

    public sealed record DriverSettlementRequest(decimal Amount, string? Note);

    /// <summary>Update driver online/offline status.</summary>
    [HttpPut("status")]
    [ProducesResponseType(typeof(ApiResponse<DriverStatusDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateStatus([FromBody] UpdateDriverStatusRequest request, CancellationToken ct)
    {
        var driver = await GetCurrentDriverAsync(ct);
        if (driver == null) return NotFound(ApiResponse.Fail("ملف السائق غير موجود"));

        driver.Status = request.Status;
        await _db.SaveChangesAsync(ct);

        return await GetStatus(ct);
    }

    /// <summary>Get active assigned order for the driver.</summary>
    [HttpGet("current-order")]
    [ProducesResponseType(typeof(ApiResponse<OrderDto?>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrentOrder(CancellationToken ct)
    {
        var driver = await GetCurrentDriverAsync(ct);
        if (driver == null) return NotFound(ApiResponse.Fail("ملف السائق غير موجود"));

        var order = await _db.Orders
            .Where(o => o.DriverId == driver.Id &&
                        o.Status != OrderStatus.Delivered &&
                        o.Status != OrderStatus.Cancelled &&
                        o.Status != OrderStatus.Returned)
            .OrderByDescending(o => o.CreatedAt)
            .Include(o => o.Customer)
                .ThenInclude(c => c.User)
            .Include(o => o.Address)
            .Include(o => o.SubOrders)
            .ThenInclude(so => so.Store)
            .Include(o => o.SubOrders)
            .ThenInclude(so => so.Items)
            .ThenInclude(item => item.Product)
            .ThenInclude(product => product!.Images)
            .FirstOrDefaultAsync(ct);

        if (order == null)
            return Ok(ApiResponse<OrderDto?>.Ok(null, "لا يوجد طلب نشط حالياً"));

        return Ok(ApiResponse<OrderDto>.Ok(MapToDto(order)));
    }

    /// <summary>Get completed order history / invoices for current driver.</summary>
    [HttpGet("orders/history")]
    [ProducesResponseType(typeof(ApiResponse<List<OrderDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrderHistory(CancellationToken ct)
    {
        var driver = await GetCurrentDriverAsync(ct);
        if (driver == null) return NotFound(ApiResponse.Fail("ملف السائق غير موجود"));

        var orders = await _db.Orders
            .Where(o => o.DriverId == driver.Id && o.Status == OrderStatus.Delivered)
            .OrderByDescending(o => o.UpdatedAt)
            .Include(o => o.Customer)
                .ThenInclude(c => c.User)
            .Include(o => o.Address)
            .Include(o => o.SubOrders)
            .ThenInclude(so => so.Store)
            .Include(o => o.SubOrders)
            .ThenInclude(so => so.Items)
            .ThenInclude(item => item.Product)
            .ThenInclude(product => product!.Images)
            .ToListAsync(ct);

        return Ok(ApiResponse<List<OrderDto>>.Ok(orders.Select(MapToDto).ToList()));
    }

    /// <summary>Accept an assigned order.</summary>
    [HttpPut("orders/{id:guid}/accept")]
    [ProducesResponseType(typeof(ApiResponse<OrderDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AcceptOrder(Guid id, CancellationToken ct)
    {
        var driver = await GetCurrentDriverAsync(ct);
        if (driver == null) return NotFound(ApiResponse.Fail("ملف السائق غير موجود"));

        var order = await _db.Orders
            .Include(o => o.SubOrders)
            .ThenInclude(so => so.Items)
            .ThenInclude(i => i.Product)
            .Include(o => o.Address)
            .Include(o => o.SubOrders)
            .ThenInclude(so => so.Store)
            .FirstOrDefaultAsync(o => o.Id == id && o.DriverId == driver.Id, ct);

        if (order == null) return NotFound(ApiResponse.Fail("الطلب غير موجود"));

        var hasExternal = order.SubOrders.SelectMany(so => so.Items)
            .Any(i => i.Product?.SourceType == SourceType.ExternalStore);

        var newStatus = hasExternal ? OrderStatus.Purchasing : OrderStatus.WarehousePickup;
        order.Status = newStatus;
        driver.Status = DriverStatus.Busy;

        order.StatusHistory.Add(new OrderStatusHistory
        {
            OldStatus = OrderStatus.DriverAssigned,
            NewStatus = newStatus,
            ActorType = "Driver",
            ActorId = driver.UserId,
            Reason = "قبول الطلب والبدء بالتنفيذ"
        });

        await _db.SaveChangesAsync(ct);
        return Ok(ApiResponse<OrderDto>.Ok(MapToDto(order), "تم قبول الطلب بنجاح"));
    }

    /// <summary>Record actual store purchase cost for a suborder.</summary>
    [HttpPost("orders/{id:guid}/record-purchase")]
    [ProducesResponseType(typeof(ApiResponse<OrderDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RecordPurchase(Guid id, [FromBody] RecordPurchaseRequest request, CancellationToken ct)
    {
        var driver = await GetCurrentDriverAsync(ct);
        if (driver == null) return NotFound(ApiResponse.Fail("ملف السائق غير موجود"));

        var order = await _db.Orders
            .Include(o => o.SubOrders)
            .ThenInclude(so => so.Items)
            .Include(o => o.Address)
            .Include(o => o.SubOrders)
            .ThenInclude(so => so.Store)
            .FirstOrDefaultAsync(o => o.Id == id && o.DriverId == driver.Id, ct);

        if (order == null) return NotFound(ApiResponse.Fail("الطلب غير موجود"));

        var subOrder = order.SubOrders.FirstOrDefault(so => so.Id == request.SubOrderId);
        if (subOrder == null) return NotFound(ApiResponse.Fail("الطلب الفرعي غير موجود"));

        subOrder.ActualPurchaseCost = request.ActualPurchasePrice;

        if (request.ItemDetails != null)
        {
            foreach (var itemDetail in request.ItemDetails)
            {
                var item = subOrder.Items.FirstOrDefault(i => i.Id == itemDetail.OrderItemId);
                if (item != null)
                {
                    item.ActualPurchasePrice = itemDetail.ActualPrice;
                }
            }
        }

        // Ledger: Record driver store purchase
        _db.DriverTransactions.Add(new DriverTransaction
        {
            DriverId = driver.Id,
            OrderId = order.Id,
            Type = DriverTransactionType.StorePurchase,
            Amount = request.ActualPurchasePrice,
            Direction = TransactionDirection.Debit,
            Reference = order.OrderNumber,
            Description = $"شراء مشتريات من متجر {subOrder.Store?.Name ?? "المتجر"}",
            CreatedBy = driver.UserId
        });

        // Check if all external suborders are purchased
        var allPurchased = order.SubOrders.All(so => so.ActualPurchaseCost.HasValue);
        if (allPurchased)
        {
            order.Status = OrderStatus.OutForDelivery;
            order.StatusHistory.Add(new OrderStatusHistory
            {
                OldStatus = OrderStatus.Purchasing,
                NewStatus = OrderStatus.OutForDelivery,
                ActorType = "Driver",
                ActorId = driver.UserId,
                Reason = "اكتمال شراء جميع الأصناف والتوجه للعميل"
            });
        }

        await _db.SaveChangesAsync(ct);
        return Ok(ApiResponse<OrderDto>.Ok(MapToDto(order), "تم تسجيل الشراء بنجاح"));
    }

    /// <summary>Confirm order delivery to customer.</summary>
    [HttpPut("orders/{id:guid}/delivered")]
    [ProducesResponseType(typeof(ApiResponse<OrderDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ConfirmDelivery(Guid id, CancellationToken ct)
    {
        var driver = await GetCurrentDriverAsync(ct);
        if (driver == null) return NotFound(ApiResponse.Fail("ملف السائق غير موجود"));

        var order = await _db.Orders
            .Include(o => o.SubOrders)
            .ThenInclude(so => so.Items)
            .Include(o => o.Payment)
            .Include(o => o.Address)
            .Include(o => o.SubOrders)
            .ThenInclude(so => so.Store)
            .FirstOrDefaultAsync(o => o.Id == id && o.DriverId == driver.Id, ct);

        if (order == null) return NotFound(ApiResponse.Fail("الطلب غير موجود"));

        order.Status = OrderStatus.Delivered;
        driver.Status = DriverStatus.Online;

        // If Cash On Delivery: driver collects total amount
        if (order.PaymentMethod == PaymentMethod.CashOnDelivery)
        {
            if (order.Payment != null)
                order.Payment.Status = PaymentStatus.Verified;

            _db.DriverTransactions.Add(new DriverTransaction
            {
                DriverId = driver.Id,
                OrderId = order.Id,
                Type = DriverTransactionType.CustomerCollection,
                Amount = order.TotalAmount,
                Direction = TransactionDirection.Credit,
                Reference = order.OrderNumber,
                Description = $"تحصيل مبلغ الطلب نقداً من العميل",
                CreatedBy = driver.UserId
            });
        }

        order.StatusHistory.Add(new OrderStatusHistory
        {
            OldStatus = OrderStatus.OutForDelivery,
            NewStatus = OrderStatus.Delivered,
            ActorType = "Driver",
            ActorId = driver.UserId,
            Reason = "تم تسليم الطلب للعميل بنجاح"
        });

        await _db.SaveChangesAsync(ct);
        return Ok(ApiResponse<OrderDto>.Ok(MapToDto(order), "تم تأكيد توصيل الطلب بنجاح"));
    }

    /// <summary>Driver financial ledger balance and summary.</summary>
    [HttpGet("financial/summary")]
    [ProducesResponseType(typeof(ApiResponse<DriverFinancialSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFinancialSummary(CancellationToken ct)
    {
        var driver = await GetCurrentDriverAsync(ct);
        if (driver == null) return NotFound(ApiResponse.Fail("ملف السائق غير موجود"));

        var transactions = await _db.DriverTransactions
            .Where(t => t.DriverId == driver.Id)
            .ToListAsync(ct);

        var collections = transactions.Where(t => t.Type == DriverTransactionType.CustomerCollection).Sum(t => t.Amount);
        var purchases = transactions.Where(t => t.Type == DriverTransactionType.StorePurchase).Sum(t => t.Amount);
        var settlements = transactions.Where(t => t.Type == DriverTransactionType.CompanySettlement).Sum(t => t.Amount);
        var fundings = transactions.Where(t => t.Type == DriverTransactionType.DriverFunding).Sum(t => t.Amount);

        // Formula: Outstanding = Collections - Purchases - Settlements + Fundings
        var outstanding = collections - purchases - settlements + fundings;

        var dto = new DriverFinancialSummaryDto(collections, purchases, settlements, fundings, outstanding);
        return Ok(ApiResponse<DriverFinancialSummaryDto>.Ok(dto));
    }

    /// <summary>Driver financial transaction history.</summary>
    [HttpGet("financial/transactions")]
    [ProducesResponseType(typeof(ApiResponse<List<DriverTransactionDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFinancialTransactions(CancellationToken ct)
    {
        var driver = await GetCurrentDriverAsync(ct);
        if (driver == null) return NotFound(ApiResponse.Fail("ملف السائق غير موجود"));

        var items = await _db.DriverTransactions
            .Where(t => t.DriverId == driver.Id)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new DriverTransactionDto(
                t.Id,
                t.Type,
                t.Amount,
                t.Direction,
                t.Reference,
                t.Description,
                t.CreatedAt
            ))
            .ToListAsync(ct);

        return Ok(ApiResponse<List<DriverTransactionDto>>.Ok(items));
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
                    i.ActualPurchasePrice,
                    i.Product?.Images.OrderBy(image => image.SortOrder).Select(image => image.Url ?? image.ImageKey).FirstOrDefault(),
                    i.ExpectedPurchasePriceSnapshot
                )).ToList(),
                so.Store?.Address,
                so.Store?.Latitude,
                so.Store?.Longitude,
                so.Store?.Phone,
                so.Store?.ImageKey
            )).ToList(),
            o.Customer?.FullName ?? "عميل المنصة",
            o.Customer?.User?.Phone ?? "",
            o.Address?.Latitude,
            o.Address?.Longitude
        );
    }
}
