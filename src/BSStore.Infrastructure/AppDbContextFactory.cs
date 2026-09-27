using BSStore.Infrastructure.Common;
using BSStore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace BSStore.Infrastructure;

/// <summary>
/// Required by EF Core tools (dotnet ef) when AppDbContext is in a different project than the startup.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // Load .env variables into Environment
        EnvLoader.Load();

        var basePath = Path.Combine(Directory.GetCurrentDirectory(), "../BSStore.API");
        if (!Directory.Exists(basePath))
        {
            basePath = Directory.GetCurrentDirectory();
        }

        var config = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        var connectionString = config.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var host = Environment.GetEnvironmentVariable("DB_HOST") ?? config["DB_HOST"] ?? "localhost";
            var port = Environment.GetEnvironmentVariable("DB_PORT") ?? config["DB_PORT"] ?? "5432";
            var db = Environment.GetEnvironmentVariable("DB_NAME") ?? config["DB_NAME"] ?? "BS_Store";
            var user = Environment.GetEnvironmentVariable("DB_USER") ?? config["DB_USER"] ?? "postgres";
            var pass = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? config["DB_PASSWORD"] ?? "";
            connectionString = $"Host={host};Port={port};Database={db};Username={user};Password={pass}";
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString,
                npgsql => npgsql.MigrationsAssembly("BSStore.Infrastructure"))
            .Options;

        return new AppDbContext(options);
    }
}
