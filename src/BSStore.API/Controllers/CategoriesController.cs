using BSStore.Application.Catalog.DTOs;
using BSStore.Application.Common;
using BSStore.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BSStore.API.Controllers;

[ApiController]
[Route("api/categories")]
[Produces("application/json")]
public class CategoriesController : ControllerBase
{
    private readonly AppDbContext _db;

    public CategoriesController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>List categories (default active only, or all if includeInactive=true).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<CategoryDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCategories([FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var query = _db.Categories.AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        var categories = await query
            .OrderBy(c => c.SortOrder)
            .Select(c => new CategoryDto(
                c.Id,
                c.Name,
                c.IconName,
                c.SortOrder,
                _db.Products.Count(p => p.CategoryId == c.Id && p.IsActive),
                c.IsActive
            ))
            .ToListAsync(ct);

        return Ok(ApiResponse<List<CategoryDto>>.Ok(categories));
    }

    /// <summary>Get products by category.</summary>
    [HttpGet("{id:guid}/products")]
    [ProducesResponseType(typeof(ApiResponse<List<ProductDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCategoryProducts(Guid id, CancellationToken ct)
    {
        var products = await _db.Products
            .Where(p => p.CategoryId == id && p.IsActive)
            .Include(p => p.Category)
            .Include(p => p.Store)
            .Include(p => p.Inventory)
            .Include(p => p.Images)
            .Select(p => new ProductDto(
                p.Id,
                p.Name,
                p.Description,
                p.CategoryId,
                p.Category.Name,
                p.StoreId,
                p.Store.Name,
                p.SourceType,
                p.ExpectedPurchasePrice,
                p.SellingPrice,
                p.Inventory != null ? p.Inventory.Quantity : null,
                p.IsActive,
                p.Images.OrderBy(i => i.SortOrder).Select(i => i.ImageKey).ToList()
            ))
            .ToListAsync(ct);

        return Ok(ApiResponse<List<ProductDto>>.Ok(products));
    }

    /// <summary>Toggle category active status.</summary>
    [HttpPatch("{id:guid}/toggle")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ToggleCategory(Guid id, CancellationToken ct)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category == null)
            return NotFound(ApiResponse.Fail("الفئة غير موجودة", "NOT_FOUND"));

        category.IsActive = !category.IsActive;
        await _db.SaveChangesAsync(ct);

        return Ok(ApiResponse<bool>.Ok(category.IsActive, category.IsActive ? "تم تفعيل الفئة" : "تم تعطيل الفئة"));
    }
}
