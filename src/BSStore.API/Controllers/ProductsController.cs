using BSStore.Application.Catalog.DTOs;
using BSStore.Application.Common;
using BSStore.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BSStore.API.Controllers;

[ApiController]
[Route("api/products")]
[Produces("application/json")]
public class ProductsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ProductsController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>List products with optional filtering and pagination.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ProductDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProducts(
        [FromQuery] Guid? categoryId,
        [FromQuery] Guid? storeId,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var query = _db.Products
            .Where(p => p.IsActive)
            .AsNoTracking();

        if (categoryId.HasValue)
            query = query.Where(p => p.CategoryId == categoryId.Value);

        if (storeId.HasValue)
            query = query.Where(p => p.StoreId == storeId.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(term) || (p.Description != null && p.Description.ToLower().Contains(term)));
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
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
                p.SellingPrice,
                p.Inventory != null ? p.Inventory.Quantity : null,
                p.IsActive,
                p.Images.OrderBy(i => i.SortOrder).Select(i => i.ImageKey).ToList()
            ))
            .ToListAsync(ct);

        var result = new PagedResult<ProductDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };

        return Ok(ApiResponse<PagedResult<ProductDto>>.Ok(result));
    }

    /// <summary>Get single product details by ID.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ProductDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProduct(Guid id, CancellationToken ct)
    {
        var p = await _db.Products
            .Where(x => x.Id == id)
            .Include(x => x.Category)
            .Include(x => x.Store)
            .Include(x => x.Inventory)
            .Include(x => x.Images)
            .FirstOrDefaultAsync(ct);

        if (p == null)
            return NotFound(ApiResponse.Fail("المنتج غير موجود", "NOT_FOUND"));

        var dto = new ProductDto(
            p.Id,
            p.Name,
            p.Description,
            p.CategoryId,
            p.Category.Name,
            p.StoreId,
            p.Store.Name,
            p.SourceType,
            p.SellingPrice,
            p.Inventory?.Quantity,
            p.IsActive,
            p.Images.OrderBy(i => i.SortOrder).Select(i => i.ImageKey).ToList()
        );

        return Ok(ApiResponse<ProductDto>.Ok(dto));
    }

    /// <summary>Quick search products by keyword.</summary>
    [HttpGet("search")]
    [ProducesResponseType(typeof(ApiResponse<List<ProductDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search([FromQuery] string q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Ok(ApiResponse<List<ProductDto>>.Ok([]));

        var term = q.Trim().ToLower();
        var products = await _db.Products
            .Where(p => p.IsActive && (p.Name.ToLower().Contains(term) || (p.Description != null && p.Description.ToLower().Contains(term))))
            .Take(20)
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
                p.SellingPrice,
                p.Inventory != null ? p.Inventory.Quantity : null,
                p.IsActive,
                p.Images.OrderBy(i => i.SortOrder).Select(i => i.ImageKey).ToList()
            ))
            .ToListAsync(ct);

        return Ok(ApiResponse<List<ProductDto>>.Ok(products));
    }
}
