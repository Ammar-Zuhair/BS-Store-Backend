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
                (deal.TryGetProperty("dealKind", out var kind) && (kind.GetString() == "BUNDLE" || kind.GetString() == "DELIVERY_DISCOUNT" || kind.GetString() == "PRODUCT_DISCOUNT")) ||
                (deal.TryGetProperty("dealType", out var type) && (type.GetString() == "BUNDLE" || type.GetString() == "DELIVERY_DISCOUNT" || type.GetString() == "PRODUCT_DISCOUNT")) ||
                (deal.TryGetProperty("products", out var products) && products.ValueKind == JsonValueKind.Array && products.GetArrayLength() > 0))
                .ToArray();
            return Ok(supportedDeals);
        }
        catch
        {
            return Ok(Array.Empty<object>());
        }
    }

    [HttpPost("validate-code")]
    public async Task<IActionResult> ValidateCouponCode([FromBody] CouponCodeRequest request, CancellationToken ct)
    {
        var code = request.Code?.Trim();
        if (string.IsNullOrWhiteSpace(code)) return BadRequest(new { message = "أدخل كود الخصم" });
        var setting = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == "FlashDeals", ct);
        if (setting == null || string.IsNullOrWhiteSpace(setting.Value)) return NotFound(new { message = "كود الخصم غير صالح أو منتهي" });
        try
        {
            using var doc = JsonDocument.Parse(setting.Value);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return NotFound(new { message = "كود الخصم غير صالح أو منتهي" });
            var deal = doc.RootElement.EnumerateArray().FirstOrDefault(item =>
                item.TryGetProperty("couponCode", out var storedCode) && storedCode.ValueKind == JsonValueKind.String &&
                string.Equals(storedCode.GetString()?.Trim(), code, StringComparison.OrdinalIgnoreCase) &&
                (!item.TryGetProperty("isActive", out var active) || active.ValueKind != JsonValueKind.False) &&
                item.TryGetProperty("expiresAt", out var expires) && DateTimeOffset.TryParse(expires.GetString(), out var expiry) && expiry > DateTimeOffset.UtcNow);
            if (deal.ValueKind != JsonValueKind.Object) return NotFound(new { message = "كود الخصم غير صالح أو منتهي" });
            var kind = deal.TryGetProperty("discountKind", out var discountKind) ? discountKind.GetString() : null;
            if (kind is not ("DELIVERY_DISCOUNT" or "PRODUCT_DISCOUNT") && deal.TryGetProperty("dealType", out var dealType)) kind = dealType.GetString();
            var percent = kind == "DELIVERY_DISCOUNT" && deal.TryGetProperty("deliveryDiscountPercent", out var deliveryPercent) && deliveryPercent.TryGetDecimal(out var parsedDeliveryPercent)
                ? parsedDeliveryPercent
                : deal.TryGetProperty("discountPercent", out var discountPercent) && discountPercent.TryGetDecimal(out var parsedDiscountPercent) ? parsedDiscountPercent : 0m;
            if (kind is not ("DELIVERY_DISCOUNT" or "PRODUCT_DISCOUNT") || percent is <= 0 or > 100)
                return NotFound(new { message = "كود الخصم غير صالح أو منتهي" });
            return Ok(new { code = code.ToUpperInvariant(), discountKind = kind, discountPercent = percent });
        }
        catch (JsonException)
        {
            return NotFound(new { message = "كود الخصم غير صالح أو منتهي" });
        }
    }

    public sealed record CouponCodeRequest(string? Code);
}
