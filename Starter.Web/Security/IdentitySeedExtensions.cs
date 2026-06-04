using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Starter.Web.Data;

namespace Starter.Web.Security;

public static class IdentitySeedExtensions
{
    private const string DevelopmentSeedPassword = "Happy1..";

    private static readonly string[] AdministratorPermissions =
        AdminPermissionCatalog.Permissions.Select(permission => permission.Key).ToArray();

    private static readonly string[] ManagerPermissions =
    [
        AdminPermissionCatalog.AccessAdmin,
        AdminPermissionCatalog.ManageUsers,
    ];

    private static readonly TestUserSeed[] DevelopmentTestUsers =
    [
        new("admin@starter.local", "Admin Tester", AdminPermissionCatalog.AdministratorRoleName),
        new("manager@starter.local", "Manager Tester", AdminPermissionCatalog.ManagerRoleName),
        new("user@starter.local", "User Tester", AdminPermissionCatalog.UserRoleName),
    ];

    private static readonly string[] DevelopmentRoleNames =
    [
        AdminPermissionCatalog.AdministratorRoleName,
        AdminPermissionCatalog.ManagerRoleName,
        AdminPermissionCatalog.UserRoleName,
    ];

    public static async Task InitializeIdentityDatabaseAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        if (dbContext.Database.GetMigrations().Any())
        {
            await dbContext.Database.MigrateAsync();
        }
        else
        {
            await dbContext.Database.EnsureCreatedAsync();
        }

        await scope.ServiceProvider.InitializeIdentityDataAsync();
    }

    public static async Task InitializeIdentityDataAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var dbContext = services.GetRequiredService<ApplicationDbContext>();
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var options = services.GetRequiredService<IOptions<IdentitySeedOptions>>().Value;
        var environment = services.GetRequiredService<IHostEnvironment>();
        var logger = services.GetRequiredService<ILogger<ApplicationDbContext>>();

        await SeedFeaturesAsync(dbContext);
        await SeedPermissionsAsync(dbContext);

        var administratorRole = await EnsureRoleAsync(
            roleManager,
            AdminPermissionCatalog.AdministratorRoleName,
            "Full access to administration.");
        await EnsureRolePermissionsAsync(dbContext, administratorRole, AdministratorPermissions);

        if (!string.IsNullOrWhiteSpace(options.AdminEmail) && !string.IsNullOrWhiteSpace(options.AdminPassword))
        {
            await EnsureConfiguredAdminUserAsync(userManager, options);
        }
        else
        {
            logger.LogInformation(
                "No configured seed administrator was found. Use the Admin setup page or configure Identity:Seed:AdminEmail and Identity:Seed:AdminPassword.");
        }

        if (environment.IsDevelopment() && options.SeedDevelopmentTestUsers)
        {
            await SeedDevelopmentTestUsersAsync(dbContext, roleManager, userManager, logger);
        }
    }

    private static async Task SeedFeaturesAsync(ApplicationDbContext dbContext)
    {
        foreach (var featureSeed in AdminPermissionCatalog.Features)
        {
            var feature = await dbContext.Features.SingleOrDefaultAsync(item => item.Key == featureSeed.Key);

            if (feature is null)
            {
                dbContext.Features.Add(new ApplicationFeature
                {
                    Key = featureSeed.Key,
                    Name = featureSeed.Name,
                    Description = featureSeed.Description,
                });
                continue;
            }

            feature.Name = featureSeed.Name;
            feature.Description = featureSeed.Description;
        }

        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedPermissionsAsync(ApplicationDbContext dbContext)
    {
        var features = await dbContext.Features.ToDictionaryAsync(feature => feature.Key);

        foreach (var permissionSeed in AdminPermissionCatalog.Permissions)
        {
            if (!features.TryGetValue(permissionSeed.FeatureKey, out var feature))
            {
                continue;
            }

            var permission = await dbContext.Permissions.SingleOrDefaultAsync(item => item.Key == permissionSeed.Key);

            if (permission is null)
            {
                dbContext.Permissions.Add(new ApplicationPermission
                {
                    Key = permissionSeed.Key,
                    Name = permissionSeed.Name,
                    Description = permissionSeed.Description,
                    FeatureId = feature.Id,
                });
                continue;
            }

            permission.Name = permissionSeed.Name;
            permission.Description = permissionSeed.Description;
            permission.FeatureId = feature.Id;
        }

        await dbContext.SaveChangesAsync();
    }

    private static async Task<ApplicationRole> EnsureRoleAsync(
        RoleManager<ApplicationRole> roleManager,
        string roleName,
        string description)
    {
        var role = await roleManager.FindByNameAsync(roleName);

        if (role is null)
        {
            role = new ApplicationRole
            {
                Name = roleName,
                Description = description,
            };

            ThrowIfFailed(await roleManager.CreateAsync(role));
            return role;
        }

        if (role.Description != description)
        {
            role.Description = description;
            ThrowIfFailed(await roleManager.UpdateAsync(role));
        }

        return role;
    }

    private static async Task EnsureRolePermissionsAsync(
        ApplicationDbContext dbContext,
        ApplicationRole role,
        IReadOnlyCollection<string> permissionKeys)
    {
        var permissionIds = await dbContext.Permissions
            .Where(permission => permissionKeys.Contains(permission.Key))
            .Select(permission => permission.Id)
            .ToListAsync();

        var assignedPermissionIds = await dbContext.RolePermissions
            .Where(rolePermission => rolePermission.RoleId == role.Id)
            .Select(rolePermission => rolePermission.PermissionId)
            .ToListAsync();

        foreach (var permissionId in permissionIds.Except(assignedPermissionIds))
        {
            dbContext.RolePermissions.Add(new ApplicationRolePermission
            {
                RoleId = role.Id,
                PermissionId = permissionId,
            });
        }

        await dbContext.SaveChangesAsync();
    }

    private static async Task EnsureConfiguredAdminUserAsync(
        UserManager<ApplicationUser> userManager,
        IdentitySeedOptions options)
    {
        var user = await userManager.FindByEmailAsync(options.AdminEmail!);

        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = options.AdminEmail,
                Email = options.AdminEmail,
                EmailConfirmed = true,
                DisplayName = options.AdminDisplayName,
                IsActive = true,
            };

            var createResult = await userManager.CreateAsync(user, options.AdminPassword!);

            ThrowIfFailed(createResult);
        }

        if (!await userManager.IsInRoleAsync(user, AdminPermissionCatalog.AdministratorRoleName))
        {
            var roleResult = await userManager.AddToRoleAsync(user, AdminPermissionCatalog.AdministratorRoleName);

            ThrowIfFailed(roleResult);
        }
    }

    private static async Task SeedDevelopmentTestUsersAsync(
        ApplicationDbContext dbContext,
        RoleManager<ApplicationRole> roleManager,
        UserManager<ApplicationUser> userManager,
        ILogger logger)
    {
        var managerRole = await EnsureRoleAsync(
            roleManager,
            AdminPermissionCatalog.ManagerRoleName,
            "Limited administration for development testing.");
        await EnsureRolePermissionsAsync(dbContext, managerRole, ManagerPermissions);

        await EnsureRoleAsync(
            roleManager,
            AdminPermissionCatalog.UserRoleName,
            "Baseline application user for development testing.");

        foreach (var userSeed in DevelopmentTestUsers)
        {
            await EnsureDevelopmentTestUserAsync(dbContext, userManager, userSeed);
        }

        logger.LogInformation(
            "Seeded development identity test users: {Emails}",
            string.Join(", ", DevelopmentTestUsers.Select(user => user.Email)));
    }

    private static async Task EnsureDevelopmentTestUserAsync(
        ApplicationDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        TestUserSeed userSeed)
    {
        var user = await userManager.FindByEmailAsync(userSeed.Email);

        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = userSeed.Email,
                Email = userSeed.Email,
                EmailConfirmed = true,
                DisplayName = userSeed.DisplayName,
                IsActive = true,
            };

            ThrowIfFailed(await userManager.CreateAsync(user, DevelopmentSeedPassword));
        }
        else
        {
            var updated = await dbContext.Users
                .Where(item => item.Id == user.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.UserName, userSeed.Email)
                    .SetProperty(item => item.NormalizedUserName, userManager.NormalizeName(userSeed.Email))
                    .SetProperty(item => item.Email, userSeed.Email)
                    .SetProperty(item => item.NormalizedEmail, userManager.NormalizeEmail(userSeed.Email))
                    .SetProperty(item => item.EmailConfirmed, true)
                    .SetProperty(item => item.DisplayName, userSeed.DisplayName)
                    .SetProperty(item => item.IsActive, true)
                    .SetProperty(item => item.ConcurrencyStamp, Guid.NewGuid().ToString()));

            if (updated == 0)
            {
                throw new InvalidOperationException($"Development test user '{userSeed.Email}' could not be updated.");
            }

            dbContext.Entry(user).State = EntityState.Detached;
            user = await userManager.FindByEmailAsync(userSeed.Email)
                ?? throw new InvalidOperationException($"Development test user '{userSeed.Email}' could not be reloaded.");

            await EnsureDevelopmentPasswordAsync(userManager, user);
        }

        await EnsureOnlyDevelopmentRoleAsync(userManager, user, userSeed.RoleName);
    }

    private static async Task EnsureDevelopmentPasswordAsync(
        UserManager<ApplicationUser> userManager,
        ApplicationUser user)
    {
        if (await userManager.CheckPasswordAsync(user, DevelopmentSeedPassword))
        {
            return;
        }

        if (!await userManager.HasPasswordAsync(user))
        {
            ThrowIfFailed(await userManager.AddPasswordAsync(user, DevelopmentSeedPassword));
            return;
        }

        var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
        ThrowIfFailed(await userManager.ResetPasswordAsync(user, resetToken, DevelopmentSeedPassword));
    }

    private static async Task EnsureOnlyDevelopmentRoleAsync(
        UserManager<ApplicationUser> userManager,
        ApplicationUser user,
        string roleName)
    {
        var currentRoles = await userManager.GetRolesAsync(user);
        var extraDevelopmentRoles = currentRoles
            .Where(currentRole => DevelopmentRoleNames.Contains(currentRole) && currentRole != roleName)
            .ToArray();

        if (extraDevelopmentRoles.Length > 0)
        {
            ThrowIfFailed(await userManager.RemoveFromRolesAsync(user, extraDevelopmentRoles));
        }

        if (!await userManager.IsInRoleAsync(user, roleName))
        {
            ThrowIfFailed(await userManager.AddToRoleAsync(user, roleName));
        }
    }

    private static void ThrowIfFailed(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => error.Description)));
        }
    }

    private sealed record TestUserSeed(string Email, string DisplayName, string RoleName);
}
