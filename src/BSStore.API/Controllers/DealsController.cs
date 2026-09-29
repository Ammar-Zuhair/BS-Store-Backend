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
            if (kind is not ("DELIVERY_DISCOUNT" or "PRODUCT_DISCOUNT"))
                return NotFound(new { message = "كود الخصم غير صالح أو منتهي" });

            decimal percent = 0m;
            var isTiered = false;
            string? message = null;
            List<object>? returnTiers = null;

            if (kind == "DELIVERY_DISCOUNT")
            {
                percent = deal.TryGetProperty("deliveryDiscountPercent", out var deliveryPercent) && deliveryPercent.TryGetDecimal(out var parsedDeliveryPercent)
                    ? parsedDeliveryPercent : 0m;
            }
            else
            {
                // PRODUCT_DISCOUNT (خصم من إجمالي الطلب)
                if (deal.TryGetProperty("discountTiers", out var tiersEl) && tiersEl.ValueKind == JsonValueKind.Array && tiersEl.GetArrayLength() > 0)
                {
                    isTiered = true;
                    returnTiers = new List<object>();
                    var tiers = new List<(decimal Min, decimal? Max, decimal Pct)>();
                    foreach (var t in tiersEl.EnumerateArray())
                    {
                        var min = t.TryGetProperty("minAmount", out var minEl) && minEl.TryGetDecimal(out var parsedMin) ? parsedMin : 0m;
                        decimal? max = t.TryGetProperty("maxAmount", out var maxEl) && maxEl.ValueKind == JsonValueKind.Number && maxEl.TryGetDecimal(out var parsedMax) ? parsedMax : null;
                        var pct = t.TryGetProperty("discountPercent", out var pctEl) && pctEl.TryGetDecimal(out var parsedPct) ? parsedPct : 0m;
                        tiers.Add((min, max, pct));
                        returnTiers.Add(new { minAmount = min, maxAmount = max, discountPercent = pct });
                    }

                    var cartSubtotal = request.CartSubtotal ?? 0m;
                    tiers = tiers.OrderBy(t => t.Min).ToList();
                    var matched = tiers.FirstOrDefault(t => cartSubtotal >= t.Min && (!t.Max.HasValue || t.Max.Value <= 0 || cartSubtotal < t.Max.Value));
                    if (matched.Pct > 0)
                    {
                        percent = matched.Pct;
                        message = $"تم تطبيق خصم {percent:G29}% للشريحة المقابلة لإجمالي سلتك ({cartSubtotal:N0} ريال)";
                    }
                    else if (tiers.Count > 0)
                    {
                        var defaultTier = cartSubtotal >= tiers.Last().Min ? tiers.Last() : tiers.First();
                        percent = defaultTier.Pct;
                        message = $"تم تفعيل كود الخصم بنسبة {percent:G29}%";
                    }
                }
                else
                {
                    percent = deal.TryGetProperty("discountPercent", out var discountPercent) && discountPercent.TryGetDecimal(out var parsedDiscountPercent) ? parsedDiscountPercent : 0m;
                }
            }

            if (percent is <= 0 or > 100)
                return NotFound(new { message = "كود الخصم غير صالح أو منتهي" });

            return Ok(new
            {
                code = code.ToUpperInvariant(),
                discountKind = kind,
                discountPercent = percent,
                isTiered,
                discountTiers = returnTiers,
                message = message ?? (kind == "DELIVERY_DISCOUNT" ? $"خصم {percent:G29}% على رسوم التوصيل" : $"خصم {percent:G29}% على إجمالي الطلب")
            });
        }
        catch (JsonException)
        {
            return NotFound(new { message = "كود الخصم غير صالح أو منتهي" });
        }
    }

    public sealed record CouponCodeRequest(string? Code, decimal? CartSubtotal = null);
}
