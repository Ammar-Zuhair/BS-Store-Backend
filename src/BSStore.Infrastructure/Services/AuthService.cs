using BSStore.Application.Auth.DTOs;
using BSStore.Application.Auth.Interfaces;
using BSStore.Domain.Entities;
using BSStore.Domain.Enums;
using BSStore.Domain.Exceptions;
using BSStore.Infrastructure.Data;
using BSStore.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace BSStore.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly IJwtTokenService _jwt;

    public AuthService(AppDbContext db, IJwtTokenService jwt)
    {
        _db = db;
        _jwt = jwt;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        // Check uniqueness
        var phoneExists = await _db.Users.AnyAsync(u => u.Phone == request.Phone, ct);
        if (phoneExists)
            throw new BusinessRuleException("رقم الهاتف مستخدم مسبقاً.", "PHONE_ALREADY_EXISTS");

        if (request.InitialAddress == null ||
            string.IsNullOrWhiteSpace(request.InitialAddress.City) ||
            string.IsNullOrWhiteSpace(request.InitialAddress.Street) ||
            request.InitialAddress.Latitude is < -90 or > 90 ||
            request.InitialAddress.Longitude is < -180 or > 180 ||
            (request.InitialAddress.Latitude == 0 && request.InitialAddress.Longitude == 0))
        {
            throw new BusinessRuleException("بيانات عنوان التوصيل وموقعه الجغرافي الصحيح إلزامية لإنشاء الحساب.", "INITIAL_ADDRESS_REQUIRED");
        }

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 10);

        var user = new User
        {
            Phone = request.Phone,
            PasswordHash = passwordHash,
            Role = UserRole.Customer
        };
        _db.Users.Add(user);

        var customer = new Customer
        {
            UserId = user.Id,
            FullName = request.FullName
        };
        _db.Customers.Add(customer);

        // Add initial mandatory address as default
        var initialAddress = new Address
        {
            Customer = customer,
            Label = request.InitialAddress.Label,
            City = request.InitialAddress.City,
            District = request.InitialAddress.District,
            Street = request.InitialAddress.Street,
            Building = request.InitialAddress.Building,
            Description = request.InitialAddress.Description,
            Latitude = request.InitialAddress.Latitude,
            Longitude = request.InitialAddress.Longitude,
            IsDefault = true
        };
        _db.Addresses.Add(initialAddress);

        // Create empty cart for customer
        _db.Carts.Add(new Cart { Customer = customer });

        await _db.SaveChangesAsync(ct);

        return await IssueTokensAsync(user, ct);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await _db.Users
            .AsNoTracking()
            .Include(u => u.Customer)
            .Include(u => u.Driver)
            .FirstOrDefaultAsync(u => u.Phone == request.Phone, ct);

        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw new BusinessRuleException("رقم الهاتف أو كلمة المرور غير صحيحة.", "INVALID_CREDENTIALS");

        if (!user.IsActive)
            throw new BusinessRuleException("الحساب موقوف. تواصل مع الإدارة.", "ACCOUNT_SUSPENDED");

        return await IssueTokensAsync(user, ct);
    }

    public async Task<AuthResponse> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken ct = default)
    {
        var tokenHash = JwtTokenService.ComputeHash(request.RefreshToken);

        var stored = await _db.RefreshTokens
            .Include(rt => rt.User)
                .ThenInclude(u => u.Customer)
            .Include(rt => rt.User)
                .ThenInclude(u => u.Driver)
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash && !rt.IsRevoked, ct);

        if (stored == null || stored.ExpiresAt < DateTime.UtcNow)
            throw new BusinessRuleException("رمز التحديث غير صالح أو منتهي الصلاحية.", "INVALID_REFRESH_TOKEN");

        // Rotate: revoke old, issue new
        stored.IsRevoked = true;

        var response = await IssueTokensAsync(stored.User, ct);
        await _db.SaveChangesAsync(ct);

        return response;
    }

    public async Task LogoutAsync(Guid userId, CancellationToken ct = default)
    {
        var tokens = await _db.RefreshTokens
            .Where(rt => rt.UserId == userId && !rt.IsRevoked)
            .ToListAsync(ct);

        foreach (var token in tokens)
            token.IsRevoked = true;

        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> CheckPhoneExistsAsync(string phone, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(phone)) return false;
        var trimmed = phone.Trim();
        return await _db.Users.AsNoTracking().AnyAsync(u => u.Phone == trimmed, ct);
    }

    // ─── Private Helpers ─────────────────────────────────────────────────────────

    private async Task<AuthResponse> IssueTokensAsync(User user, CancellationToken ct)
    {
        var roleName = user.Role.ToString();
        var accessToken = _jwt.GenerateAccessToken(user.Id, user.Phone, roleName);
        var (rawRefreshToken, tokenHash, expiry) = _jwt.GenerateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = tokenHash,
            ExpiresAt = expiry
        });

        await _db.SaveChangesAsync(ct);

        var fullName = user.Customer?.FullName
            ?? user.Driver?.FullName
            ?? (user.Role == UserRole.SuperAdmin || user.Role == UserRole.Admin ? "مدير النظام" : "مستخدم");

        return new AuthResponse(
            AccessToken: accessToken,
            RefreshToken: rawRefreshToken,
            User: new UserDto(user.Id, fullName, user.Phone, roleName)
        );
    }
}
