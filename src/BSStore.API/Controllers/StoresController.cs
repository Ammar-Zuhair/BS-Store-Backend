using BSStore.Application.Catalog.DTOs;
using BSStore.Application.Common;
using BSStore.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BSStore.API.Controllers;

[ApiController]
[Route("api/stores")]
[Produces("application/json")]
public class StoresController : ControllerBase
{
    private readonly AppDbContext _db;

    public StoresController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>List stores (default active only, or all if includeInactive=true).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<StoreDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStores([FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var query = _db.Stores.AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(s => s.IsActive);
        }

        var stores = await query
            .OrderBy(s => s.Name)
            .Select(s => new StoreDto(
                s.Id,
                s.Name,
                s.Phone,
                s.Address,
                s.Latitude,
                s.Longitude,
                s.ImageKey,
                s.IsActive
            ))
            .ToListAsync(ct);

        return Ok(ApiResponse<List<StoreDto>>.Ok(stores));
    }

    /// <summary>Get store details by ID.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<StoreDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStore(Guid id, CancellationToken ct)
    {
        var store = await _db.Stores
            .Where(s => s.Id == id)
            .Select(s => new StoreDto(
                s.Id,
                s.Name,
                s.Phone,
                s.Address,
                s.Latitude,
                s.Longitude,
                s.ImageKey,
                s.IsActive
            ))
            .FirstOrDefaultAsync(ct);

        if (store == null)
            return NotFound(ApiResponse.Fail("المتجر غير موجود", "NOT_FOUND"));

        return Ok(ApiResponse<StoreDto>.Ok(store));
    }

    /// <summary>Toggle store active status.</summary>
    [HttpPatch("{id:guid}/toggle")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ToggleStore(Guid id, CancellationToken ct)
    {
        var store = await _db.Stores.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (store == null)
            return NotFound(ApiResponse.Fail("المتجر غير موجود", "NOT_FOUND"));

        store.IsActive = !store.IsActive;
        await _db.SaveChangesAsync(ct);

        return Ok(ApiResponse<bool>.Ok(store.IsActive, store.IsActive ? "تم تفعيل المتجر" : "تم تعطيل المتجر"));
    }
}
