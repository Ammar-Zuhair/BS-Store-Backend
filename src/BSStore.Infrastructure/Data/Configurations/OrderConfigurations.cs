using BSStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BSStore.Infrastructure.Data.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(o => o.Id);
        builder.Property(o => o.OrderNumber).HasMaxLength(20).IsRequired();
        builder.HasIndex(o => o.OrderNumber).IsUnique();
        builder.HasIndex(o => o.IdempotencyKey).IsUnique().HasFilter("\"IdempotencyKey\" IS NOT NULL");
        builder.Property(o => o.Status).HasConversion<string>().IsRequired();
        builder.Property(o => o.PaymentMethod).HasConversion<string>().IsRequired();
        builder.Property(o => o.PaymentStatus).HasConversion<string>().IsRequired();
        builder.Property(o => o.SubTotal).HasPrecision(18, 2);
        builder.Property(o => o.DeliveryFee).HasPrecision(18, 2);
        builder.Property(o => o.Discount).HasPrecision(18, 2);
        builder.Property(o => o.TotalAmount).HasPrecision(18, 2);
        builder.HasIndex(o => new { o.CustomerId, o.CreatedAt });
        builder.HasIndex(o => new { o.Status, o.CreatedAt });
        builder.HasIndex(o => o.DriverId);

        builder.HasOne(o => o.Payment)
               .WithOne(p => p.Order)
               .HasForeignKey<Payment>(p => p.OrderId);

        builder.HasOne(o => o.Cancellation)
               .WithOne(c => c.Order)
               .HasForeignKey<OrderCancellation>(c => c.OrderId);
    }
}

public class SubOrderConfiguration : IEntityTypeConfiguration<SubOrder>
{
    public void Configure(EntityTypeBuilder<SubOrder> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Status).HasConversion<string>().IsRequired();
        builder.Property(s => s.ExpectedPurchaseCost).HasPrecision(18, 2);
        builder.Property(s => s.ActualPurchaseCost).HasPrecision(18, 2);
        builder.Property(s => s.SubTotal).HasPrecision(18, 2);
        builder.HasIndex(s => s.OrderId);

        builder.HasMany(s => s.Items)
               .WithOne(i => i.SubOrder)
               .HasForeignKey(i => i.SubOrderId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.ProductNameSnapshot).HasMaxLength(300).IsRequired();
        builder.Property(i => i.SellingPriceSnapshot).HasPrecision(18, 2);
        builder.Property(i => i.ExpectedPurchasePriceSnapshot).HasPrecision(18, 2);
        builder.Property(i => i.ExpectedProfitSnapshot).HasPrecision(18, 2);
        builder.Property(i => i.ActualPurchasePrice).HasPrecision(18, 2);
        builder.Property(i => i.TotalSellingPrice).HasPrecision(18, 2);
    }
}

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Method).HasConversion<string>().IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().IsRequired();
        builder.Property(p => p.Amount).HasPrecision(18, 2);
        builder.HasIndex(p => p.Status);

        builder.HasMany(p => p.Receipts)
               .WithOne(r => r.Payment)
               .HasForeignKey(r => r.PaymentId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
