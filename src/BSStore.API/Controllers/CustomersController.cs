using System.Security.Claims;
using BSStore.Application.Common;
using BSStore.Domain.Entities;
using BSStore.Domain.Enums;
using BSStore.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BSStore.API.Controllers;

public record CreateAddressRequest(
    AddressLabel Label,
    string City,
    string District,
    string Street,
    string? Building,
    string? Description,
    decimal Latitude,
    decimal Longitude,
    bool IsDefault
);

public record AddressDto(
    Guid Id,
    AddressLabel Label,
    string City,
    string District,
    string Street,
    string? Building,
    string? Description,
    decimal Latitude,
    decimal Longitude,
    bool IsDefault
);

[ApiController]
[Route("api/customers")]
[Authorize]
[Produces("application/json")]
public class CustomersController : ControllerBase
{
    private readonly AppDbContext _db;

    public CustomersController(AppDbContext db)
    {
        _db = db;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue("userId")!);

    private async Task<Customer?> GetCustomerAsync(CancellationToken ct)
    {
        var userId = GetUserId();
        return await _db.Customers
            .Include(c => c.Addresses)
            .FirstOrDefaultAsync(c => c.UserId == userId, ct);
    }

    /// <summary>Get all saved addresses for current customer.</summary>
    [HttpGet("addresses")]
    [ProducesResponseType(typeof(ApiResponse<List<AddressDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAddresses(CancellationToken ct)
    {
        var customer = await GetCustomerAsync(ct);
        if (customer == null) return Unauthorized(ApiResponse.Fail("حساب العميل غير موجود"));

        var dtos = customer.Addresses
            .OrderByDescending(a => a.IsDefault)
            .ThenByDescending(a => a.CreatedAt)
            .Select(a => new AddressDto(
                a.Id,
                a.Label,
                a.City,
                a.District,
                a.Street,
                a.Building,
                a.Description,
                a.Latitude,
                a.Longitude,
                a.IsDefault
            ))
            .ToList();

        return Ok(ApiResponse<List<AddressDto>>.Ok(dtos));
    }

    /// <summary>Add new address for current customer.</summary>
    [HttpPost("addresses")]
    [ProducesResponseType(typeof(ApiResponse<AddressDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AddAddress([FromBody] CreateAddressRequest request, CancellationToken ct)
    {
        var customer = await GetCustomerAsync(ct);
        if (customer == null) return Unauthorized(ApiResponse.Fail("حساب العميل غير موجود"));

        if (string.IsNullOrWhiteSpace(request.City) || string.IsNullOrWhiteSpace(request.Street))
            return BadRequest(ApiResponse.Fail("المدينة والشارع حقول إلزامية"));

        if (request.IsDefault)
        {
            foreach (var existing in customer.Addresses)
            {
                existing.IsDefault = false;
            }
        }

        var isFirst = customer.Addresses.Count == 0;
        var address = new Address
        {
            CustomerId = customer.Id,
            Label = request.Label,
            City = request.City,
            District = request.District,
            Street = request.Street,
            Building = request.Building,
            Description = request.Description,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            IsDefault = request.IsDefault || isFirst
        };

        _db.Addresses.Add(address);
        await _db.SaveChangesAsync(ct);

        var dto = new AddressDto(
            address.Id,
            address.Label,
            address.City,
            address.District,
            address.Street,
            address.Building,
            address.Description,
            address.Latitude,
            address.Longitude,
            address.IsDefault
        );

        return Ok(ApiResponse<AddressDto>.Ok(dto, "تم إضافة العنوان بنجاح"));
    }

    /// <summary>Delete an address.</summary>
    [HttpDelete("addresses/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteAddress(Guid id, CancellationToken ct)
    {
        var customer = await GetCustomerAsync(ct);
        if (customer == null) return Unauthorized(ApiResponse.Fail("حساب العميل غير موجود"));

        var address = customer.Addresses.FirstOrDefault(a => a.Id == id);
        if (address == null) return NotFound(ApiResponse.Fail("العنوان غير موجود"));

        _db.Addresses.Remove(address);
        await _db.SaveChangesAsync(ct);

        return Ok(ApiResponse.Ok("تم حذف العنوان بنجاح"));
    }

    /// <summary>Set an address as default.</summary>
    [HttpPut("addresses/{id:guid}/default")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetDefaultAddress(Guid id, CancellationToken ct)
    {
        var customer = await GetCustomerAsync(ct);
        if (customer == null) return Unauthorized(ApiResponse.Fail("حساب العميل غير موجود"));

        foreach (var addr in customer.Addresses)
        {
            addr.IsDefault = addr.Id == id;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(ApiResponse.Ok("تم تعيين العنوان كافتراضي"));
    }
}
