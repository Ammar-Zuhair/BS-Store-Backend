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
            return Ok(Array.Empty<object>());
        }

        try
        {
            using var doc = JsonDocument.Parse(setting.Value);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return Ok(Array.Empty<object>());

            var now = DateTimeOffset.UtcNow;
            var visibleDeals = doc.RootElement.EnumerateArray()
                .Where(deal =>
                    (!deal.TryGetProperty("isActive", out var active) || active.ValueKind != JsonValueKind.False) &&
                    (!deal.TryGetProperty("isPublic", out var isPublic) || isPublic.ValueKind != JsonValueKind.False) &&
                    deal.TryGetProperty("expiresAt", out var expiry) &&
                    expiry.ValueKind == JsonValueKind.String &&
                    DateTimeOffset.TryParse(expiry.GetString(), out var parsedExpiry) && parsedExpiry > now)
                .Select(deal => deal.Clone())
                .ToArray();

            var supportedDeals = visibleDeals.Where(deal =>
                (deal.TryGetProperty("dealKind", out var kind) && (kind.GetString() == "BUNDLE" || kind.GetString() == "DELIVERY_DISCOUNT")) ||
                (deal.TryGetProperty("dealType", out var type) && (type.GetString() == "BUNDLE" || type.GetString() == "DELIVERY_DISCOUNT")) ||
                (deal.TryGetProperty("products", out var products) && products.ValueKind == JsonValueKind.Array && products.GetArrayLength() > 0))
                .ToArray();
            return Ok(supportedDeals);
        }
        catch
        {
            return Ok(Array.Empty<object>());
        }
    }
}
