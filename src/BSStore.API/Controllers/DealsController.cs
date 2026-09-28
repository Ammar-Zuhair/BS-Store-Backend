using System.Text.Json;
using BSStore.Application.Common;
using BSStore.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BSStore.API.Controllers;

[ApiController]
[Route("api/deals")]
[Produces("application/json")]
public class DealsController : ControllerBase
{
    private readonly AppDbContext _db;

    public DealsController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>List active promotional deals and bundles for customer catalog.</summary>
    [HttpGet]
    public async Task<IActionResult> GetDeals(
        [FromQuery] string? categoryId = null,
        [FromQuery] string? storeId = null,
        CancellationToken ct = default)
    {
        var setting = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == "FlashDeals", ct);
        if (setting == null || string.IsNullOrWhiteSpace(setting.Value))
        {
            var defaultDeals = AdminController.GetDefaultDeals();
            return Ok(defaultDeals);
        }

        try
        {
            using var doc = JsonDocument.Parse(setting.Value);
            return Ok(doc.RootElement.Clone());
        }
        catch
        {
            return Ok(AdminController.GetDefaultDeals());
        }
    }
}
