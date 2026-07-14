using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using System.Threading.RateLimiting;
using StackExchange.Redis;
using Starter.Web;
using Starter.Web.Components;
using Starter.Web.Data;
using Starter.Web.Navigation;
using Starter.Web.Security;
using Starter.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();
builder.AddRedisOutputCache("cache");

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("account", limiterOptions =>
    {
        limiterOptions.PermitLimit = 5;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueLimit = 0;
    });
});

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
var cacheConnectionString = builder.Configuration.GetConnectionString("cache")
    ?? throw new InvalidOperationException("Connection string 'cache' was not found.");
var redisConnection = ConnectionMultiplexer.Connect($"{cacheConnectionString},abortConnect=false");
builder.Services.AddSingleton<IConnectionMultiplexer>(redisConnection);
builder.Services.AddDataProtection()
    .SetApplicationName("Starter.Web")
    .PersistKeysToStackExchangeRedis(redisConnection, "Starter.Web:DataProtectionKeys");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("starterdb")
        ?? throw new InvalidOperationException("Connection string 'starterdb' was not found.");

    options.UseNpgsql(connectionString);
});
builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/admin/login";
    options.AccessDeniedPath = "/admin/access-denied";
});
builder.Services.Configure<IdentitySeedOptions>(builder.Configuration.GetSection("Identity:Seed"));
builder.Services.AddScoped<AdminIdentityService>();
builder.Services.AddScoped<SystemConfigurationService>();
builder.Services.AddScoped<AgentConversationStore>();
builder.Services.AddScoped<IAccountEmailSender, SmtpAccountEmailSender>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AdminPolicies.AccessAdmin, policy =>
        policy.Requirements.Add(new PermissionRequirement(AdminPermissionCatalog.AccessAdmin)));
    options.AddPolicy(AdminPolicies.ManageUsers, policy =>
        policy.Requirements.Add(new PermissionRequirement(AdminPermissionCatalog.ManageUsers)));
    options.AddPolicy(AdminPolicies.ManageRoles, policy =>
        policy.Requirements.Add(new PermissionRequirement(AdminPermissionCatalog.ManageRoles)));
    options.AddPolicy(AdminPolicies.ManagePermissions, policy =>
        policy.Requirements.Add(new PermissionRequirement(AdminPermissionCatalog.ManagePermissions)));
    options.AddPolicy(AdminPolicies.ManageFeatures, policy =>
        policy.Requirements.Add(new PermissionRequirement(AdminPermissionCatalog.ManageFeatures)));
    options.AddPolicy(AdminPolicies.ManageSystem, policy =>
        policy.Requirements.Add(new PermissionRequirement(AdminPermissionCatalog.ManageSystem)));
});

// MudBlazor + app-shell services.
builder.Services.AddMudServices();
builder.Services.AddSingleton<NavRegistry>();
builder.Services.AddScoped<ShellStateService>();

builder.Services.AddHttpClient<WeatherApiClient>(client =>
    {
        // This URL uses "https+http://" to indicate HTTPS is preferred over HTTP.
        // Learn more about service discovery scheme resolution at https://aka.ms/dotnet/sdschemes.
        client.BaseAddress = new("https+http://apiservice");
    });
builder.Services.AddHttpClient<CatalogApiClient>(client =>
    {
        client.BaseAddress = new("https+http://apiservice");
    });
builder.Services.AddHttpClient<AiChatService>();
builder.Services.AddHttpClient<AiModelCatalogService>();
builder.Services.AddHttpClient<AgentToolService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseRateLimiter();

app.UseAntiforgery();

app.UseOutputCache();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAdminIdentityEndpoints();
app.MapAccountIdentityEndpoints();

app.MapDefaultEndpoints();

app.Run();
