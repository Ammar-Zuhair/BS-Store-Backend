using BSStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BSStore.Infrastructure.Data.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.IconName).HasMaxLength(100).IsRequired();
        builder.HasIndex(c => c.IsActive);
    }
}

public class StoreConfiguration : IEntityTypeConfiguration<Store>
{
    public void Configure(EntityTypeBuilder<Store> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Phone).HasMaxLength(20).IsRequired();
        builder.Property(s => s.Latitude).HasPrecision(10, 7);
        builder.Property(s => s.Longitude).HasPrecision(10, 7);
        builder.HasIndex(s => s.IsActive);
    }
}

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(300).IsRequired();
        builder.Property(p => p.SourceType).HasConversion<string>().IsRequired();
        builder.Property(p => p.ExpectedPurchasePrice).HasPrecision(18, 2);
        builder.Property(p => p.SellingPrice).HasPrecision(18, 2);
        builder.HasIndex(p => new { p.CategoryId, p.IsActive, p.IsDeleted });
        builder.HasIndex(p => new { p.StoreId, p.IsActive });
        builder.HasQueryFilter(p => !p.IsDeleted); // Global soft-delete filter

        builder.HasMany(p => p.Images)
               .WithOne(i => i.Product)
               .HasForeignKey(i => i.ProductId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.Inventory)
               .WithOne(inv => inv.Product)
               .HasForeignKey<Inventory>(inv => inv.ProductId)
               .IsRequired(false);
    }
}

public class InventoryConfiguration : IEntityTypeConfiguration<Inventory>
{
    public void Configure(EntityTypeBuilder<Inventory> builder)
    {
        builder.HasKey(i => i.Id);
        builder.HasIndex(i => i.ProductId).IsUnique();
        // Inventory.Transactions navigation is configured on InventoryTransactionConfiguration
    }
}

public class InventoryTransactionConfiguration : IEntityTypeConfiguration<InventoryTransaction>
{
    public void Configure(EntityTypeBuilder<InventoryTransaction> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Type).HasConversion<string>().IsRequired();
        builder.HasIndex(t => new { t.ProductId, t.CreatedAt });
        builder.HasIndex(t => t.OrderId);
    }
}
