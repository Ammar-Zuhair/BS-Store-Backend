using BSStore.Domain.Enums;

namespace BSStore.Application.Auth.DTOs;

public record InitialAddressDto(
    string City,
    string District,
    string Street,
    string? Building,
    string? Description,
    decimal Latitude,
    decimal Longitude,
    AddressLabel Label = AddressLabel.Home
);

public record RegisterRequest(
    string FullName,
    string Phone,
    string Password,
    InitialAddressDto? InitialAddress = null
);

public record LoginRequest(
    string Phone,
    string Password
);

public record RefreshTokenRequest(
    string RefreshToken
);

public record AuthResponse(
    string AccessToken,
    string RefreshToken,
    UserDto User
);

public record UserDto(
    Guid Id,
    string FullName,
    string Phone,
    string Role
);
