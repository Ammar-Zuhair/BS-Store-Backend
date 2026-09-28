using BSStore.Application.Common;
using BSStore.Domain.Entities;
using BSStore.Domain.Enums;
using BSStore.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BSStore.API.Controllers;

[ApiController]
[Route("api/driver-applications")]
[AllowAnonymous]
public sealed class DriverApplicationsController : ControllerBase
{
    private readonly AppDbContext _db;
    public DriverApplicationsController(AppDbContext db) => _db = db;

    [HttpPost]
    public async Task<IActionResult> Register([FromBody] DriverApplicationRequest request, CancellationToken ct)
    {
        var fullName = request.FullName?.Trim();
        var phone = request.Phone?.Trim();
        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            return BadRequest(ApiResponse.Fail("أدخل الاسم ورقم الهاتف وكلمة مرور من 8 أحرف على الأقل"));
        if (await _db.Users.AnyAsync(u => u.Phone == phone, ct))
            return Conflict(ApiResponse.Fail("رقم الهاتف مسجل مسبقاً"));

        var user = new User { Phone = phone, PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 10), Role = UserRole.Driver, IsActive = false };
        var driver = new Driver { User = user, FullName = fullName, Phone = phone, Status = DriverStatus.Offline };
        _db.Users.Add(user);
        _db.Drivers.Add(driver);
        await _db.SaveChangesAsync(ct);
        return Accepted(ApiResponse.Ok("تم إرسال طلب الموصل إلى الإدارة للموافقة"));
    }

    public sealed record DriverApplicationRequest(string? FullName, string? Phone, string? Password);
}
