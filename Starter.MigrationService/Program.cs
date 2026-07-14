using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
    .AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddRoles<ApplicationRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.Configure<IdentitySeedOptions>(builder.Configuration.GetSection("Identity:Seed"));
builder.Services.AddScoped<SystemConfigurationService>();
builder.Services.AddHostedService<Worker>();

await builder.Build().RunAsync();

internal sealed class Worker(
    IServiceProvider serviceProvider,
    IHostApplicationLifetime hostApplicationLifetime,
    IConfiguration configuration,
    IHostEnvironment hostEnvironment,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var applicationDbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var catalogDbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

            logger.LogInformation("Applying application database migrations.");
            await applicationDbContext.Database.MigrateAsync(stoppingToken);

            logger.LogInformation("Applying API catalog database migrations.");
            await catalogDbContext.Database.MigrateAsync(stoppingToken);

            await scope.ServiceProvider.InitializeIdentityDataAsync(stoppingToken);
            await scope.ServiceProvider.InitializeSystemConfigurationAsync(stoppingToken);

            if (hostEnvironment.IsDevelopment()
                && configuration.GetValue("Catalog:Seed:SampleData", true))
            {
                await catalogDbContext.SeedCatalogSampleDataAsync(logger, stoppingToken);
            }
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
}
