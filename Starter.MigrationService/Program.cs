using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Data;
using Starter.ApiService.Data;
using Starter.Web.Data;
using Starter.Web.Security;
using Starter.Web.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.AddNpgsqlDbContext<ApplicationDbContext>("starterdb");
builder.AddNpgsqlDbContext<DevTodoDbContext>("starterdb");

builder.Services.AddDataProtection();
builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddRoles<ApplicationRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.Configure<IdentitySeedOptions>(builder.Configuration.GetSection("Identity:Seed"));
builder.Services.AddScoped<SystemConfigurationService>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

await host.RunAsync();

internal sealed class Worker(
    IServiceProvider serviceProvider,
    IHostApplicationLifetime hostApplicationLifetime,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var applicationDbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var devTodoDbContext = scope.ServiceProvider.GetRequiredService<DevTodoDbContext>();

            await BaselineExistingEnsureCreatedDatabaseAsync(applicationDbContext, logger, stoppingToken);

            logger.LogInformation("Applying application database migrations.");
            await applicationDbContext.Database.MigrateAsync(stoppingToken);

            logger.LogInformation("Applying API CRUD database migrations.");
            await devTodoDbContext.Database.MigrateAsync(stoppingToken);

            logger.LogInformation("Seeding identity and admin data.");
            await scope.ServiceProvider.InitializeIdentityDataAsync(stoppingToken);

            logger.LogInformation("Seeding system configuration.");
            await scope.ServiceProvider.InitializeSystemConfigurationAsync(stoppingToken);

            logger.LogInformation("Database migration service completed.");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Database migration service was canceled.");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Database migration service failed.");
            Environment.ExitCode = 1;
        }
        finally
        {
            hostApplicationLifetime.StopApplication();
        }
    }

    private static async Task BaselineExistingEnsureCreatedDatabaseAsync(
        DbContext dbContext,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var migrations = dbContext.Database.GetMigrations().ToArray();

        if (migrations.Length == 0
            || await TableExistsAsync(dbContext, "__EFMigrationsHistory", cancellationToken)
            || !await TableExistsAsync(dbContext, "AspNetUsers", cancellationToken))
        {
            return;
        }

        logger.LogWarning(
            "Existing Identity tables were found without EF migration history. Marking existing schema as baseline.");

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                "MigrationId" character varying(150) NOT NULL,
                "ProductVersion" character varying(32) NOT NULL,
                CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
            );
            """,
            cancellationToken);

        var efCoreVersion = typeof(DbContext).Assembly.GetName().Version?.ToString(3) ?? "10.0.0";

        foreach (var migration in migrations)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES ({migration}, {efCoreVersion})
                ON CONFLICT ("MigrationId") DO NOTHING;
                """,
                cancellationToken);
        }
    }

    private static async Task<bool> TableExistsAsync(
        DbContext dbContext,
        string tableName,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT EXISTS (
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = 'public'
                      AND table_name = @tableName
                );
                """;

            var parameter = command.CreateParameter();
            parameter.ParameterName = "@tableName";
            parameter.Value = tableName;
            command.Parameters.Add(parameter);

            return await command.ExecuteScalarAsync(cancellationToken) is true;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }
}
