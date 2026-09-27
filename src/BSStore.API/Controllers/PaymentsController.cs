using BSStore.Application.Common;
using BSStore.Application.Common.Interfaces;
using BSStore.Application.Payments.DTOs;
using BSStore.Domain.Entities;
using BSStore.Domain.Enums;
using BSStore.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BSStore.API.Controllers;

[ApiController]
[Route("api/payments")]
[Authorize]
[Produces("application/json")]
public class PaymentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IStorageService _storageService;

    public PaymentsController(AppDbContext db, IStorageService storageService)
    {
        _db = db;
        _storageService = storageService;
    }

    /// <summary>Get bank transfer account information and payment status for an order.</summary>
    [HttpGet("{orderId:guid}/transfer-info")]
    [ProducesResponseType(typeof(ApiResponse<TransferInfoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTransferInfo(Guid orderId, CancellationToken ct)
    {
        var order = await _db.Orders
            .Include(o => o.Payment)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

        if (order == null || order.Payment == null)
            return NotFound(ApiResponse.Fail("الطلب أو بيانات الدفع غير موجودة"));

        var settings = await _db.PaymentSettings.FirstOrDefaultAsync(ct)
            ?? new PaymentSettings
            {
                TransferBankName = "بنك الكريمي",
                TransferAccountNumber = "123456789",
                TransferAccountName = "مؤسسة BS Store للتجارة"
            };

        var dto = new TransferInfoDto(
            order.Id,
            order.OrderNumber,
            order.TotalAmount,
            settings.TransferBankName ?? "بنك الكريمي",
            settings.TransferAccountNumber ?? "123456789",
            settings.TransferAccountName ?? "مؤسسة BS Store",
            order.Payment.Status,
            order.Payment.RejectionReason
        );

        return Ok(ApiResponse<TransferInfoDto>.Ok(dto));
    }

    /// <summary>Upload bank transfer payment receipt image/document.</summary>
    [HttpPost("{orderId:guid}/upload-receipt")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<UploadReceiptResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadReceipt(Guid orderId, IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse.Fail("يرجى إرفاق ملف الإشعار"));

        // Max 5MB
        if (file.Length > 5 * 1024 * 1024)
            return BadRequest(ApiResponse.Fail("حجم الملف يتجاوز الحد المسموح (5 ميجابايت)"));

        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".pdf" };
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(ext))
            return BadRequest(ApiResponse.Fail("نوع الملف غير مسموح. المسموح: JPG, PNG, PDF"));

        var order = await _db.Orders
            .Include(o => o.Payment)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

        if (order == null || order.Payment == null)
            return NotFound(ApiResponse.Fail("الطلب غير موجود"));

        // Upload file
        using var stream = file.OpenReadStream();
        var storageKey = await _storageService.UploadFileAsync(stream, file.FileName, file.ContentType, "receipts", ct);
        var fileUrl = _storageService.GetFileUrl(storageKey);

        // Record receipt
        var receipt = new PaymentReceipt
        {
            PaymentId = order.Payment.Id,
            StorageKey = storageKey,
            Url = fileUrl,
            MimeType = file.ContentType,
            FileSize = file.Length
        };
        _db.PaymentReceipts.Add(receipt);

        // Update payment and order status
        order.Payment.Status = PaymentStatus.PendingVerification;
        order.Status = OrderStatus.PaymentPendingVerification;

        await _db.SaveChangesAsync(ct);

        return Ok(ApiResponse<UploadReceiptResponse>.Ok(
            new UploadReceiptResponse(receipt.Id, fileUrl, order.Payment.Status),
            "تم رفع إشعار التحويل بنجاح، سيتم تدقيقه من قبل الإدارة"
        ));
    }
}
