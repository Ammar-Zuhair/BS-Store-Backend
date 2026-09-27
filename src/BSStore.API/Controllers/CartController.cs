using System.Security.Claims;
using BSStore.Application.Cart.DTOs;
using BSStore.Application.Common;
using BSStore.Domain.Entities;
using BSStore.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BSStore.API.Controllers;

[ApiController]
[Route("api/cart")]
[Authorize]
[Produces("application/json")]
public class CartController : ControllerBase
{
    private readonly AppDbContext _db;

    public CartController(AppDbContext db)
    {
        _db = db;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue("userId")!);

    private async Task<Cart> GetOrCreateCustomerCartAsync(CancellationToken ct)
    {
        var userId = GetUserId();
        var customer = await _db.Customers
            .Include(c => c.Cart)
            .ThenInclude(cart => cart!.Items)
            .ThenInclude(item => item.Product)
            .ThenInclude(p => p.Store)
            .FirstOrDefaultAsync(c => c.UserId == userId, ct);

        if (customer == null)
            throw new InvalidOperationException("حساب العميل غير موجود");

        if (customer.Cart == null)
        {
            var newCart = new Cart { CustomerId = customer.Id };
            _db.Carts.Add(newCart);
            await _db.SaveChangesAsync(ct);
            return newCart;
        }

        return customer.Cart;
    }

    /// <summary>Get current customer cart.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<CartDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCart(CancellationToken ct)
    {
        var cart = await GetOrCreateCustomerCartAsync(ct);

        var items = cart.Items.Select(i => new CartItemDto(
            i.Id,
            i.ProductId,
            i.Product.Name,
            i.Product.Store?.Name,
            i.Product.SellingPrice,
            i.Quantity,
            i.Product.SellingPrice * i.Quantity,
            i.Notes,
            null,
            i.Product.SourceType
        )).ToList();

        var subTotal = items.Sum(i => i.TotalPrice);
        var totalItems = items.Sum(i => i.Quantity);

        return Ok(ApiResponse<CartDto>.Ok(new CartDto(cart.Id, items, subTotal, totalItems)));
    }

    /// <summary>Add item to cart.</summary>
    [HttpPost("items")]
    [ProducesResponseType(typeof(ApiResponse<CartDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AddItem([FromBody] AddToCartRequest request, CancellationToken ct)
    {
        if (request.Quantity <= 0)
            return BadRequest(ApiResponse.Fail("الكمية يجب أن تكون أكبر من صفر"));

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId && p.IsActive, ct);
        if (product == null)
            return NotFound(ApiResponse.Fail("المنتج غير موجود أو غير متاح حالياً"));

        var cart = await GetOrCreateCustomerCartAsync(ct);
        var existingItem = cart.Items.FirstOrDefault(i => i.ProductId == request.ProductId);

        if (existingItem != null)
        {
            existingItem.Quantity += request.Quantity;
            if (!string.IsNullOrWhiteSpace(request.Notes))
                existingItem.Notes = request.Notes;
        }
        else
        {
            var newItem = new CartItem
            {
                CartId = cart.Id,
                ProductId = request.ProductId,
                Quantity = request.Quantity,
                Notes = request.Notes
            };
            _db.CartItems.Add(newItem);
        }

        await _db.SaveChangesAsync(ct);
        return await GetCart(ct);
    }

    /// <summary>Update cart item quantity or notes.</summary>
    [HttpPut("items/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<CartDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateItem(Guid id, [FromBody] UpdateCartItemRequest request, CancellationToken ct)
    {
        var cart = await GetOrCreateCustomerCartAsync(ct);
        var item = cart.Items.FirstOrDefault(i => i.Id == id);
        if (item == null)
            return NotFound(ApiResponse.Fail("الصنف غير موجود في السلة"));

        if (request.Quantity <= 0)
        {
            _db.CartItems.Remove(item);
        }
        else
        {
            item.Quantity = request.Quantity;
            item.Notes = request.Notes;
        }

        await _db.SaveChangesAsync(ct);
        return await GetCart(ct);
    }

    /// <summary>Remove single item from cart.</summary>
    [HttpDelete("items/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<CartDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveItem(Guid id, CancellationToken ct)
    {
        var cart = await GetOrCreateCustomerCartAsync(ct);
        var item = cart.Items.FirstOrDefault(i => i.Id == id);
        if (item != null)
        {
            _db.CartItems.Remove(item);
            await _db.SaveChangesAsync(ct);
        }

        return await GetCart(ct);
    }

    /// <summary>Clear entire cart.</summary>
    [HttpDelete]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ClearCart(CancellationToken ct)
    {
        var cart = await GetOrCreateCustomerCartAsync(ct);
        _db.CartItems.RemoveRange(cart.Items);
        await _db.SaveChangesAsync(ct);

        return Ok(ApiResponse.Ok("تم إفراغ السلة بنجاح"));
    }
}
