using System.Security.Claims;
using BSStore.Application.Auth.DTOs;
using BSStore.Application.Auth.Interfaces;
using BSStore.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BSStore.API.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>Check if a phone number already exists in the system.</summary>
    [HttpGet("check-phone")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckPhone([FromQuery] string phone, CancellationToken ct)
    {
        var exists = await _authService.CheckPhoneExistsAsync(phone, ct);
        return Ok(ApiResponse<bool>.Ok(exists));
    }

    /// <summary>Register a new customer account.</summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        var result = await _authService.RegisterAsync(request, ct);
        return Ok(ApiResponse<AuthResponse>.Ok(result, "تم التسجيل بنجاح"));
    }

    /// <summary>Login with phone and password.</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await _authService.LoginAsync(request, ct);
        return Ok(ApiResponse<AuthResponse>.Ok(result, "تم تسجيل الدخول بنجاح"));
    }

    /// <summary>Refresh access token using a valid refresh token.</summary>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, CancellationToken ct)
    {
        var result = await _authService.RefreshTokenAsync(request, ct);
        return Ok(ApiResponse<AuthResponse>.Ok(result));
    }

    /// <summary>Logout and revoke all refresh tokens.</summary>
    [Authorize]
    [HttpPost("logout")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue("userId")!);
        await _authService.LogoutAsync(userId, ct);
        return Ok(ApiResponse.Ok("تم تسجيل الخروج بنجاح"));
    }

    /// <summary>Get current authenticated user info.</summary>
    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status200OK)]
    public IActionResult Me()
    {
        var userId = Guid.Parse(User.FindFirstValue("userId")!);
        var phone = User.FindFirstValue(ClaimTypes.MobilePhone) ?? "";
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";
        var name = User.FindFirstValue(ClaimTypes.Name) ?? "";

        return Ok(ApiResponse<UserDto>.Ok(new UserDto(userId, name, phone, role)));
    }
}
