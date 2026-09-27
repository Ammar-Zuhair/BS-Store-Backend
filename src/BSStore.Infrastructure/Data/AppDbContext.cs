using BSStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BSStore.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // Auth
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // People
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<Address> Addresses => Set<Address>();

    // Catalog
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();

    // Inventory
    public DbSet<Inventory> Inventories => Set<Inventory>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();

    // Cart
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();

    // Orders
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();
    public DbSet<SubOrder> SubOrders => Set<SubOrder>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    // Payments
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentReceipt> PaymentReceipts => Set<PaymentReceipt>();

    // Driver Financial
    public DbSet<DriverTransaction> DriverTransactions => Set<DriverTransaction>();
    public DbSet<DriverSettlement> DriverSettlements => Set<DriverSettlement>();

    // Notifications
    public DbSet<Notification> Notifications => Set<Notification>();

    // Settings
    public DbSet<DeliverySettings> DeliverySettings => Set<DeliverySettings>();
    public DbSet<PaymentSettings> PaymentSettings => Set<PaymentSettings>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    // Cancellations
    public DbSet<CancellationReason> CancellationReasons => Set<CancellationReason>();
    public DbSet<OrderCancellation> OrderCancellations => Set<OrderCancellation>();

    // Audit
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // Fraud Prevention
    public DbSet<FlaggedUser> FlaggedUsers => Set<FlaggedUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all entity configurations from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is Domain.Common.BaseEntity entity)
            {
                if (entry.State == EntityState.Added && entity.Id == Guid.Empty)
                    entity.Id = Guid.NewGuid();

                if (entry.State == EntityState.Modified)
                    entity.UpdatedAt = DateTime.UtcNow;
            }
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
