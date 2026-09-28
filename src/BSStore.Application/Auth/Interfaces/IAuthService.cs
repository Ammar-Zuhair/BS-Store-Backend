using BSStore.Application.Auth.DTOs;

namespace BSStore.Application.Auth.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<AuthResponse> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken ct = default);
    Task LogoutAsync(Guid userId, CancellationToken ct = default);
    Task<bool> CheckPhoneExistsAsync(string phone, CancellationToken ct = default);
}

public interface IJwtTokenService
{
    string GenerateAccessToken(Guid userId, string phone, string role);
    (string token, string hash, DateTime expiry) GenerateRefreshToken();
    Guid? GetUserIdFromToken(string token);
}
