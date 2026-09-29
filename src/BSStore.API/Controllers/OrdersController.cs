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
[Route("api/orders")]
[Authorize]
[Produces("application/json")]
public class OrdersController : ControllerBase
{
    private readonly AppDbContext _db;

    public OrdersController(AppDbContext db)
    {
        _db = db;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue("userId")!);

    [HttpPost("{id:guid}/review")]
    public async Task<IActionResult> SubmitReview(Guid id, [FromBody] OrderReviewRequest request, CancellationToken ct)
    {
        if (request.Rating is < 1 or > 5 || string.IsNullOrWhiteSpace(request.Comment))
            return BadRequest(ApiResponse.Fail("اختر تقييماً من نجمة إلى خمس واكتب ملاحظتك"));
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.UserId == GetUserId(), ct);
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id && customer != null && o.CustomerId == customer.Id, ct);
        if (order == null) return NotFound(ApiResponse.Fail("الطلب غير موجود"));
        if (order.Status != OrderStatus.Delivered) return BadRequest(ApiResponse.Fail("يمكن تقييم الطلب بعد توصيله فقط"));
        if (order.CustomerRating.HasValue) return Conflict(ApiResponse.Fail("سبق لك تقييم هذا الطلب"));
        order.CustomerRating = request.Rating;
        order.CustomerReview = request.Comment.Trim();
        order.CustomerRatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(ApiResponse.Ok("شكراً لتقييمك"));
    }

    public sealed record OrderReviewRequest(int Rating, string Comment);

    /// <summary>Get list of orders for current customer.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<OrderDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyOrders(CancellationToken ct)
    {
        var userId = GetUserId();
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (customer == null)
            return Unauthorized(ApiResponse.Fail("حساب العميل غير موجود"));

        var orders = await _db.Orders
            .Where(o => o.CustomerId == customer.Id)
            .OrderByDescending(o => o.CreatedAt)
            .Include(o => o.Customer)
                .ThenInclude(c => c.User)
            .Include(o => o.Driver)
            .Include(o => o.Address)
            .Include(o => o.SubOrders)
            .ThenInclude(so => so.Store)
            .Include(o => o.SubOrders)
            .ThenInclude(so => so.Items)
            .ThenInclude(item => item.Product)
            .ThenInclude(product => product!.Images)
            .ToListAsync(ct);

        var dtos = orders.Select(MapToDto).ToList();
        return Ok(ApiResponse<List<OrderDto>>.Ok(dtos));
    }

    /// <summary>Get order details by ID.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<OrderDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrderDetails(Guid id, CancellationToken ct)
    {
        var userId = GetUserId();
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.UserId == userId, ct);

        var order = await _db.Orders
            .Where(o => o.Id == id)
            .Include(o => o.Customer)
                .ThenInclude(c => c.User)
            .Include(o => o.Driver)
            .Include(o => o.Address)
            .Include(o => o.SubOrders)
            .ThenInclude(so => so.Store)
            .Include(o => o.SubOrders)
            .ThenInclude(so => so.Items)
            .ThenInclude(item => item.Product)
            .ThenInclude(product => product!.Images)
            .FirstOrDefaultAsync(ct);

        if (order == null)
            return NotFound(ApiResponse.Fail("الطلب غير موجود"));

        // Only owner customer, driver, or admin can view
        var isCustomer = customer != null && order.CustomerId == customer.Id;
        var isAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
        var isDriver = User.IsInRole("Driver") && order.DriverId != null;

        if (!isCustomer && !isAdmin && !isDriver)
            return Forbid();

        return Ok(ApiResponse<OrderDto>.Ok(MapToDto(order)));
    }

    /// <summary>Cancel order (allowed before driver acceptance or for admin).</summary>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CancelOrder(Guid id, [FromBody] CancelOrderRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.UserId == userId, ct);

        var order = await _db.Orders
            .Include(o => o.SubOrders)
            .ThenInclude(so => so.Items)
            .ThenInclude(i => i.Product)
            .ThenInclude(p => p!.Inventory)
            .FirstOrDefaultAsync(o => o.Id == id, ct);

        if (order == null)
            return NotFound(ApiResponse.Fail("الطلب غير موجود"));

        var isAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
        if (!isAdmin && (customer == null || order.CustomerId != customer.Id))
            return Forbid();

        if (order.Status == OrderStatus.Delivered || order.Status == OrderStatus.Cancelled || order.Status == OrderStatus.OutForDelivery)
        {
            return BadRequest(ApiResponse.Fail("لا يمكن إلغاء الطلب في حالته الحالية"));
        }

        using var tx = await _db.Database.BeginTransactionAsync(ct);

        var oldStatus = order.Status;
        order.Status = OrderStatus.Cancelled;

        // Release reserved inventory for InStock products
        foreach (var subOrder in order.SubOrders)
        {
            subOrder.Status = OrderStatus.Cancelled;
            foreach (var item in subOrder.Items)
            {
                if (item.Product != null && item.Product.SourceType == SourceType.InStock && item.Product.Inventory != null)
                {
                    item.Product.Inventory.Quantity += item.Quantity;
                    _db.InventoryTransactions.Add(new InventoryTransaction
                    {
                        ProductId = item.Product.Id,
                        OrderId = order.Id,
                        Type = InventoryTransactionType.Release,
                        Quantity = item.Quantity,
                        Note = $"إلغاء حجز كمية لإلغاء الطلب {order.OrderNumber}"
                    });
                }
            }
        }

        // Record OrderCancellation
        _db.OrderCancellations.Add(new OrderCancellation
        {
            OrderId = order.Id,
            ReasonCode = request.ReasonCode,
            Description = request.Description,
            CancelledBy = userId
        });

        // Record history
        order.StatusHistory.Add(new OrderStatusHistory
        {
            OldStatus = oldStatus,
            NewStatus = OrderStatus.Cancelled,
            ActorType = isAdmin ? "Admin" : "Customer",
            ActorId = userId,
            Reason = request.Description ?? request.ReasonCode
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Ok(ApiResponse.Ok("تم إلغاء الطلب بنجاح"));
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
                    i.Product?.Images.OrderBy(image => image.SortOrder).Select(image => image.Url ?? image.ImageKey).FirstOrDefault()
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
            o.Address?.Longitude,
            o.CustomerRating,
            o.CustomerReview,
            o.CustomerRatedAt
        );
    }
}
