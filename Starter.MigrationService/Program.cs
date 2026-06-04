using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Globalization;
using Starter.ApiService.Data;
using Starter.Web.Data;
using Starter.Web.Security;
using Starter.Web.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.AddNpgsqlDbContext<ApplicationDbContext>("starterdb");
builder.AddNpgsqlDbContext<CatalogDbContext>("starterdb");

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
    IConfiguration configuration,
    IHostEnvironment hostEnvironment,
    ILogger<Worker> logger) : BackgroundService
{
    private const string EfMigrationsHistoryTable = "__EFMigrationsHistory";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var applicationDbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var catalogDbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

            await BaselineExistingEnsureCreatedDatabaseAsync(applicationDbContext, logger, stoppingToken);

            logger.LogInformation("Applying application database migrations.");
            await applicationDbContext.Database.MigrateAsync(stoppingToken);

            logger.LogInformation("Applying API catalog database migrations.");
            await catalogDbContext.Database.MigrateAsync(stoppingToken);

            logger.LogInformation("Seeding identity and admin data.");
            await scope.ServiceProvider.InitializeIdentityDataAsync(stoppingToken);

            logger.LogInformation("Seeding system configuration.");
            await scope.ServiceProvider.InitializeSystemConfigurationAsync(stoppingToken);

            if (hostEnvironment.IsDevelopment()
                && configuration.GetValue("Catalog:Seed:SampleData", true))
            {
                logger.LogInformation("Seeding catalog sample data.");
                await catalogDbContext.SeedCatalogSampleDataAsync(logger, stoppingToken);
            }

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
            || await TableExistsAsync(dbContext, EfMigrationsHistoryTable, cancellationToken)
            || !await TableExistsAsync(dbContext, "AspNetUsers", cancellationToken))
        {
            return;
        }

        logger.LogWarning(
            "Existing Identity tables were found without EF migration history. Marking existing schema as baseline.");

        await CreateMigrationsHistoryTableAsync(dbContext, cancellationToken);

        var efCoreVersion = typeof(DbContext).Assembly.GetName().Version?.ToString(3) ?? "10.0.0";

        foreach (var migration in migrations)
        {
            await InsertMigrationHistoryAsync(dbContext, migration, efCoreVersion, cancellationToken);
        }
    }

    private static Task CreateMigrationsHistoryTableAsync(
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (IsSqlServer(dbContext))
        {
            return dbContext.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID(N'[dbo].[__EFMigrationsHistory]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[__EFMigrationsHistory] (
                        [MigrationId] nvarchar(150) NOT NULL,
                        [ProductVersion] nvarchar(32) NOT NULL,
                        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
                    );
                END
                """,
                cancellationToken);
        }

        return dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                "MigrationId" character varying(150) NOT NULL,
                "ProductVersion" character varying(32) NOT NULL,
                CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
            );
            """,
            cancellationToken);
    }

    private static Task InsertMigrationHistoryAsync(
        DbContext dbContext,
        string migration,
        string efCoreVersion,
        CancellationToken cancellationToken)
    {
        if (IsSqlServer(dbContext))
        {
            return dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                IF NOT EXISTS (
                    SELECT 1
                    FROM [dbo].[__EFMigrationsHistory]
                    WHERE [MigrationId] = {migration}
                )
                BEGIN
                    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                    VALUES ({migration}, {efCoreVersion});
                END
                """,
                cancellationToken);
        }

        return dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES ({migration}, {efCoreVersion})
            ON CONFLICT ("MigrationId") DO NOTHING;
            """,
            cancellationToken);
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
            command.CommandText = IsSqlServer(dbContext)
                ? """
                  SELECT CAST(CASE WHEN EXISTS (
                      SELECT 1
                      FROM INFORMATION_SCHEMA.TABLES
                      WHERE TABLE_SCHEMA = 'dbo'
                        AND TABLE_NAME = @tableName
                  ) THEN 1 ELSE 0 END AS bit);
                  """
                : """
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

            return IsTruthy(await command.ExecuteScalarAsync(cancellationToken));
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static bool IsSqlServer(DbContext dbContext) =>
        string.Equals(
            dbContext.Database.ProviderName,
            "Microsoft.EntityFrameworkCore.SqlServer",
            StringComparison.Ordinal);

    private static bool IsTruthy(object? value) =>
        value switch
        {
            null => false,
            bool boolean => boolean,
            byte number => number != 0,
            short number => number != 0,
            int number => number != 0,
            long number => number != 0,
            decimal number => number != 0,
            _ => Convert.ToBoolean(value, CultureInfo.InvariantCulture)
        };
}
