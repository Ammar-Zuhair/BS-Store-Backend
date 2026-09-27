using BSStore.Domain.Entities;
using BSStore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BSStore.Infrastructure.Data;

public static class DataSeeder
{
    public static async Task SeedAsync(AppDbContext context, ILogger logger)
    {
        // 1. Delivery Settings
        var existingDelivery = await context.DeliverySettings.FirstOrDefaultAsync();
        if (existingDelivery == null)
        {
            logger.LogInformation("Seeding DeliverySettings...");
            context.DeliverySettings.Add(new DeliverySettings
            {
                Id = 1,
                IsEnabled = true,
                BaseFee = 400,
                PerKmFee = 50,
                BaseDistanceKm = 1.0m,
                FreeDeliveryRadius = 0
            });
            await context.SaveChangesAsync();
        }
        else
        {
            existingDelivery.BaseFee = 400;
            existingDelivery.PerKmFee = 50;
            existingDelivery.BaseDistanceKm = 1.0m;
            await context.SaveChangesAsync();
        }

        // 2. Payment Settings
        if (!await context.PaymentSettings.AnyAsync())
        {
            logger.LogInformation("Seeding PaymentSettings...");
            context.PaymentSettings.Add(new PaymentSettings
            {
                Id = 1,
                CashOnDeliveryLimit = 50000,
                TransferBankName = "بنك الكريمي للتمويل الأصغر الإسلامي",
                TransferAccountNumber = "123456789",
                TransferAccountName = "مؤسسة BS Store للتجارة والتوصيل"
            });
            await context.SaveChangesAsync();
        }

        // 3. Cancellation Reasons
        if (!await context.CancellationReasons.AnyAsync())
        {
            logger.LogInformation("Seeding CancellationReasons...");
            context.CancellationReasons.AddRange(
                new CancellationReason { Code = "CHANGED_MIND", Label = "تغيير رأي العميل", IsActive = true },
                new CancellationReason { Code = "DELIVERY_DELAYED", Label = "تأخر موعد التوصيل", IsActive = true },
                new CancellationReason { Code = "OUT_OF_STOCK", Label = "نفاد كمية أحد الأصناف", IsActive = true },
                new CancellationReason { Code = "WRONG_ADDRESS", Label = "خطأ في عنوان التوصيل", IsActive = true },
                new CancellationReason { Code = "OTHER", Label = "سبب آخر", IsActive = true }
            );
            await context.SaveChangesAsync();
        }

        // 4. Admin User
        var adminPhone = "777000000";
        if (!await context.Users.AnyAsync(u => u.Phone == adminPhone))
        {
            logger.LogInformation("Seeding Admin User...");
            var adminUser = new User
            {
                Phone = adminPhone,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("AdminPassword123!", 12),
                Role = UserRole.SuperAdmin,
                IsActive = true
            };
            context.Users.Add(adminUser);
            await context.SaveChangesAsync();
        }

        // 5. Driver User
        var driverPhone = "777000222";
        if (!await context.Users.AnyAsync(u => u.Phone == driverPhone))
        {
            logger.LogInformation("Seeding Demo Driver...");
            var driverUser = new User
            {
                Phone = driverPhone,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("DriverPassword123!", 12),
                Role = UserRole.Driver,
                IsActive = true
            };
            context.Users.Add(driverUser);
            await context.SaveChangesAsync();

            var driverProfile = new Driver
            {
                UserId = driverUser.Id,
                FullName = "الكابتن أحمد المحمدي",
                Phone = driverPhone,
                Status = DriverStatus.Online
            };
            context.Drivers.Add(driverProfile);
            await context.SaveChangesAsync();
        }

        // 6. Categories, Stores, Products
        if (!await context.Categories.AnyAsync())
        {
            logger.LogInformation("Seeding Categories, Stores, and Products...");

            var catFood = new Category { Name = "مطاعم وكافيهات", IconName = "silverware-fork-knife", SortOrder = 1, IsActive = true };
            var catSupermarket = new Category { Name = "سوبرماركت وبقالة", IconName = "cart-outline", SortOrder = 2, IsActive = true };
            var catPharmacy = new Category { Name = "صيدليات وعناية", IconName = "medical-bag", SortOrder = 3, IsActive = true };
            var catElectronics = new Category { Name = "إلكترونيات وهواتف", IconName = "cellphone", SortOrder = 4, IsActive = true };

            context.Categories.AddRange(catFood, catSupermarket, catPharmacy, catElectronics);
            await context.SaveChangesAsync();

            var store1 = new Store
            {
                Name = "مركز السعيد التجاري",
                Phone = "01234567",
                Address = "شارع حدة - تقاطع القدس",
                Latitude = 15.3400000m,
                Longitude = 44.2000000m,
                IsActive = true
            };
            var store2 = new Store
            {
                Name = "مطعم الشيباني الفاخر",
                Phone = "01987654",
                Address = "شارع الجزائر - جوار النفق",
                Latitude = 15.3500000m,
                Longitude = 44.2100000m,
                IsActive = true
            };
            var store3 = new Store
            {
                Name = "صيدلية النور النموذجية",
                Phone = "01555444",
                Address = "شارع الستين الجنوبي",
                Latitude = 15.3350000m,
                Longitude = 44.1950000m,
                IsActive = true
            };

            context.Stores.AddRange(store1, store2, store3);
            await context.SaveChangesAsync();

            // Products
            var prod1 = new Product
            {
                Name = "أرز بسمتي درجة أولى 5 كجم",
                Description = "أرز هندي حبة طويلة أصلي عالي الجودة",
                CategoryId = catSupermarket.Id,
                StoreId = store1.Id,
                SourceType = SourceType.InStock,
                ExpectedPurchasePrice = 4500,
                SellingPrice = 5200,
                IsActive = true
            };
            var prod2 = new Product
            {
                Name = "زيت طبخ نقي 1.5 لتر",
                Description = "زيت ذرة نقي وخفيف مناسب لجميع الطبخات",
                CategoryId = catSupermarket.Id,
                StoreId = store1.Id,
                SourceType = SourceType.InStock,
                ExpectedPurchasePrice = 2800,
                SellingPrice = 3300,
                IsActive = true
            };
            var prod3 = new Product
            {
                Name = "وجبة مشكل مشاوي عائلي",
                Description = "كباب وأوصال مع الخبز والمقبلات الطازجة",
                CategoryId = catFood.Id,
                StoreId = store2.Id,
                SourceType = SourceType.ExternalStore,
                ExpectedPurchasePrice = 7000,
                SellingPrice = 8500,
                IsActive = true
            };
            var prod4 = new Product
            {
                Name = "فيتامين سي 1000 ملغ فوار",
                Description = "أقراص فوارة لدعم المناعة ومقاومة الإرهاق",
                CategoryId = catPharmacy.Id,
                StoreId = store3.Id,
                SourceType = SourceType.ExternalStore,
                ExpectedPurchasePrice = 1200,
                SellingPrice = 1600,
                IsActive = true
            };

            context.Products.AddRange(prod1, prod2, prod3, prod4);
            await context.SaveChangesAsync();

            // Inventories for InStock products
            context.Inventories.AddRange(
                new Inventory { ProductId = prod1.Id, Quantity = 50 },
                new Inventory { ProductId = prod2.Id, Quantity = 80 }
            );

            // Also add initial stock-in transactions for audit
            context.InventoryTransactions.AddRange(
                new InventoryTransaction
                {
                    ProductId = prod1.Id,
                    Type = InventoryTransactionType.StockIn,
                    Quantity = 50,
                    Note = "رصيد افتتاحي للمخزن"
                },
                new InventoryTransaction
                {
                    ProductId = prod2.Id,
                    Type = InventoryTransactionType.StockIn,
                    Quantity = 80,
                    Note = "رصيد افتتاحي للمخزن"
                }
            );

            await context.SaveChangesAsync();
            logger.LogInformation("Database seeded successfully with initial data.");
        }
    }
}
