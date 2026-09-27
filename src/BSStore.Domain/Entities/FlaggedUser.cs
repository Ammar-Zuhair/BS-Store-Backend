using BSStore.Domain.Common;

namespace BSStore.Domain.Entities;

/// <summary>
/// يمثل رقم مُبلَّغاً عنه أو محظوراً بسبب سلوك احتيالي.
/// يُستخدم للحد من عمليات الإلغاء المتكررة والاحتيال.
/// </summary>
public class FlaggedUser : BaseEntity
{
    /// <summary>اسم العميل كما ظهر في الطلب</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>رقم الهاتف المُبلَّغ عنه</summary>
    public string Phone { get; set; } = string.Empty;

    /// <summary>سبب الإبلاغ الذي حدده المدير</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>هل الرقم محظور كلياً؟</summary>
    public bool IsBanned { get; set; } = false;

    /// <summary>عدد مرات إلغاء الطلبات</summary>
    public int CancelCount { get; set; } = 1;

    /// <summary>آخر طلب مُبلَّغ عنه</summary>
    public Guid? LastOrderId { get; set; }

    /// <summary>المدير الذي أضاف التحذير</summary>
    public string FlaggedByAdminName { get; set; } = string.Empty;

    /// <summary>ملاحظات إضافية</summary>
    public string Notes { get; set; } = string.Empty;
}
