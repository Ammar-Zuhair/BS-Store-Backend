using System.Text;
using BSStore.API.Middleware;
using BSStore.Application.Auth.Interfaces;
using BSStore.Application.Common.Interfaces;
using BSStore.Infrastructure.Common;
using BSStore.Infrastructure.Data;
using BSStore.Infrastructure.Identity;
using BSStore.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

// ─── 0. Load .env environment variables early ─────────────────────────────────
EnvLoader.Load();

// ─── Bootstrap Serilog early ───────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Ensure environment variables are loaded into configuration
    builder.Configuration.AddEnvironmentVariables();

    // Ensure Kestrel binds to 0.0.0.0 and honors PORT on hosting platforms (like Render/Railway)
    var appPort = Environment.GetEnvironmentVariable("PORT") ?? "5295";
    builder.WebHost.UseUrls($"http://0.0.0.0:{appPort}");

    // ─── Serilog ───────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((ctx, services, cfg) =>
    {
        cfg.ReadFrom.Configuration(ctx.Configuration)
           .ReadFrom.Services(services)
           .Enrich.FromLogContext()
           .WriteTo.Console()
           .WriteTo.File("logs/bsstore-.log",
               rollingInterval: RollingInterval.Day,
               retainedFileCountLimit: 7);
    });

    // ─── Database Connection String Resolution ─────────────────────────────────
    var rawConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    string connectionString;

    if (string.IsNullOrWhiteSpace(rawConnectionString))
    {
        var host = builder.Configuration["DB_HOST"] ?? "localhost";
        var dbPort = builder.Configuration["DB_PORT"] ?? "5432";
        var db = builder.Configuration["DB_NAME"] ?? "BS_Store";
        var user = builder.Configuration["DB_USER"] ?? "postgres";
        var pass = builder.Configuration["DB_PASSWORD"] ?? "";
        connectionString = $"Host={host};Port={dbPort};Database={db};Username={user};Password={pass};";
    }
    else if (rawConnectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) || 
             rawConnectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        // Parse standard URI format provided by Supabase / Cloud Postgres
        try
        {
            var uri = new Uri(rawConnectionString);
            var userInfo = uri.UserInfo.Split(':');
            var user = Uri.UnescapeDataString(userInfo[0]);
            var pass = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
            var host = uri.Host;
            var uriPort = uri.Port > 0 ? uri.Port : 5432;
            var db = uri.AbsolutePath.TrimStart('/');
            connectionString = $"Host={host};Port={uriPort};Database={db};Username={user};Password={pass};SSL Mode=Require;Trust Server Certificate=true;";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to parse database URI. Using raw connection string.");
            connectionString = rawConnectionString;
        }
    }
    else
    {
        connectionString = rawConnectionString;
    }

    // ─── Optimize Connection Pooling for Cloud Database (Supabase / Render) ────
    try
    {
        var npgsqlBuilder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = true,
            MinPoolSize = 1,                 // Keep 1 warm connection ready to eliminate SSL/TCP handshake latency
            MaxPoolSize = 25,                // Avoid exhausting connection limits on Supabase free-tier
            ConnectionIdleLifetime = 300,   // Keep active in pool up to 5 minutes
            KeepAlive = 30,                 // Send TCP keepalive probe every 30s to prevent cloud proxy/firewall drops
            Timeout = 15,                   // Fail-fast timeout for initial connection attempt
            CommandTimeout = 30
        };

        if (npgsqlBuilder.Host != "localhost" && npgsqlBuilder.Host != "127.0.0.1")
        {
            npgsqlBuilder.SslMode = Npgsql.SslMode.Require;
        }

        connectionString = npgsqlBuilder.ConnectionString;
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Failed to apply connection pooling optimizations. Using default connection string.");
    }

    builder.Services.AddDbContext<AppDbContext>(options =>
    {
        options.UseNpgsql(
            connectionString,
            npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly("BSStore.Infrastructure");
                npgsqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(3),
                    errorCodesToAdd: null);
            }
        );

        if (builder.Environment.IsDevelopment())
            options.EnableSensitiveDataLogging();
    });

    // ─── JWT Authentication ────────────────────────────────────────────────────
    var jwtSecret = builder.Configuration["Jwt:Secret"];
    if (string.IsNullOrWhiteSpace(jwtSecret))
    {
        jwtSecret = "BSStore_Dev_Super_Secret_Key_32_Characters_Minimum_Secure_2026!";
    }

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "BSStore.API",
                ValidAudience = builder.Configuration["Jwt:Audience"] ?? "BSStore.Apps",
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
                ClockSkew = TimeSpan.Zero
            };

            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = ctx =>
                {
                    Log.Warning("JWT authentication failed: {Error}", ctx.Exception.Message);
                    return Task.CompletedTask;
                }
            };
        });

    builder.Services.AddAuthorization();

    // ─── Application & Infrastructure Services ─────────────────────────────────
    builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
    builder.Services.AddScoped<IAuthService, AuthService>();
    builder.Services.AddScoped<IStorageService, LocalStorageService>();

    // ─── CORS ──────────────────────────────────────────────────────────────────
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("MobileApps", policy =>
        {
            policy.SetIsOriginAllowed(_ => true)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        });
    });

    // ─── Controllers & Swagger ─────────────────────────────────────────────────
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "BS Store API",
            Version = "v1",
            Description = "منصة الوساطة والتوصيل — API Documentation"
        });

        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "Bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "أدخل JWT token: Bearer {token}"
        });

        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });
    });

    // ─── Rate Limiting ─────────────────────────────────────────────────────────
    builder.Services.AddRateLimiter(options =>
    {
        options.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        {
            var key = ctx.User?.FindFirst("userId")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon";
            return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(key, _ =>
                new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                {
                    PermitLimit = 120,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                });
        });
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    });

    // ─── Build App ─────────────────────────────────────────────────────────────
    var app = builder.Build();

    // ─── Static files for uploads ──────────────────────────────────────────────
    var webRoot = app.Environment.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
    var uploadsDir = Path.Combine(webRoot, "uploads");
    if (!Directory.Exists(uploadsDir))
    {
        Directory.CreateDirectory(uploadsDir);
    }

    app.UseStaticFiles(); // Serves wwwroot
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(uploadsDir),
        RequestPath = "/uploads"
    });

    // ─── Middleware Pipeline ───────────────────────────────────────────────────
    app.UseMiddleware<ExceptionHandlingMiddleware>();
    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "BS Store API v1");
            c.RoutePrefix = "swagger";
        });
    }

    app.UseCors("MobileApps");
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    // ─── Fast Health Check Endpoint (Keep-Alive for Render / Monitoring) ────────
    app.MapGet("/health", () => Results.Ok(new
    {
        status = "healthy",
        service = "BS Store API",
        timestamp = DateTime.UtcNow
    }));
    app.MapGet("/api/health", () => Results.Ok(new
    {
        status = "healthy",
        service = "BS Store API",
        timestamp = DateTime.UtcNow
    }));

    // ─── Programmatic Restart Endpoint (Graceful shutdown, auto-restarted by systemd) ───
    app.MapPost("/api/system/restart", (IHostApplicationLifetime lifetime) =>
    {
        Log.Information("System restart requested via /api/system/restart endpoint.");
        Task.Run(async () =>
        {
            await Task.Delay(500);
            lifetime.StopApplication();
        });
        return Results.Ok(new { message = "Restarting backend service..." });
    });

    app.MapControllers();

    // ─── Auto-migrate and Seed on startup ──────────────────────────────────────
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            await db.Database.MigrateAsync();
            Log.Information("Database migration applied successfully.");

            await DataSeeder.SeedAsync(db, logger);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error while applying migration or seeding database.");
        }
    }

    Log.Information("BS Store API starting on {Environment}", app.Environment.EnvironmentName);
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application startup failed.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
