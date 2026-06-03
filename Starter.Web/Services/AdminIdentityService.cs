using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Starter.Web.Data;
using Starter.Web.Security;

namespace Starter.Web.Services;

public sealed class AdminIdentityService(IServiceScopeFactory scopeFactory)
{
    public async Task<AdminDashboardSummary> GetDashboardAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return new AdminDashboardSummary(
            await dbContext.Users.CountAsync(),
            await dbContext.Roles.CountAsync(),
            await dbContext.Permissions.CountAsync(),
            await dbContext.Features.CountAsync());
    }

    public async Task<bool> HasAnyUsersAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await dbContext.Users.AnyAsync();
    }

    public async Task<IReadOnlyList<FeatureSummary>> GetFeaturesAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await dbContext.Features
            .AsNoTracking()
            .OrderBy(feature => feature.Name)
            .Select(feature => new FeatureSummary(
                feature.Id,
                feature.Key,
                feature.Name,
                feature.Description,
                feature.IsEnabled,
                feature.Permissions.Count))
            .ToListAsync();
    }

    public async Task<AdminMutationResult> SaveFeatureAsync(FeatureFormModel model)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var key = NormalizeKey(model.Key);

        var duplicateExists = await dbContext.Features
            .AnyAsync(feature => feature.Key == key && feature.Id != model.Id);

        if (duplicateExists)
        {
            return AdminMutationResult.Failure("A feature with this key already exists.");
        }

        ApplicationFeature? feature = null;

        if (model.Id is not null)
        {
            feature = await dbContext.Features.FindAsync(model.Id.Value);

            if (feature is null)
            {
                return AdminMutationResult.Failure("Feature not found.");
            }
        }

        feature ??= new ApplicationFeature();
        feature.Key = key;
        feature.Name = model.Name.Trim();
        feature.Description = NormalizeOptional(model.Description);
        feature.IsEnabled = model.IsEnabled;

        if (model.Id is null)
        {
            dbContext.Features.Add(feature);
        }

        await dbContext.SaveChangesAsync();
        return AdminMutationResult.Success("Feature saved.");
    }

    public async Task<AdminMutationResult> DeleteFeatureAsync(int id)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var feature = await dbContext.Features
            .Include(item => item.Permissions)
            .SingleOrDefaultAsync(item => item.Id == id);

        if (feature is null)
        {
            return AdminMutationResult.Failure("Feature not found.");
        }

        if (feature.Permissions.Count > 0)
        {
            return AdminMutationResult.Failure("Remove this feature's permissions first.");
        }

        dbContext.Features.Remove(feature);
        await dbContext.SaveChangesAsync();

        return AdminMutationResult.Success("Feature deleted.");
    }

    public async Task<IReadOnlyList<PermissionSummary>> GetPermissionsAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await dbContext.Permissions
            .AsNoTracking()
            .Include(permission => permission.Feature)
            .OrderBy(permission => permission.Feature.Name)
            .ThenBy(permission => permission.Name)
            .Select(permission => new PermissionSummary(
                permission.Id,
                permission.Key,
                permission.Name,
                permission.Description,
                permission.FeatureId,
                permission.Feature.Name,
                permission.Feature.Key,
                permission.RolePermissions.Count))
            .ToListAsync();
    }

    public async Task<AdminMutationResult> SavePermissionAsync(PermissionFormModel model)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var key = NormalizeKey(model.Key);

        var featureExists = await dbContext.Features.AnyAsync(feature => feature.Id == model.FeatureId);

        if (!featureExists)
        {
            return AdminMutationResult.Failure("Feature not found.");
        }

        var duplicateExists = await dbContext.Permissions
            .AnyAsync(permission => permission.Key == key && permission.Id != model.Id);

        if (duplicateExists)
        {
            return AdminMutationResult.Failure("A permission with this key already exists.");
        }

        ApplicationPermission? permission = null;

        if (model.Id is not null)
        {
            permission = await dbContext.Permissions.FindAsync(model.Id.Value);

            if (permission is null)
            {
                return AdminMutationResult.Failure("Permission not found.");
            }
        }

        permission ??= new ApplicationPermission();
        permission.Key = key;
        permission.Name = model.Name.Trim();
        permission.Description = NormalizeOptional(model.Description);
        permission.FeatureId = model.FeatureId;

        if (model.Id is null)
        {
            dbContext.Permissions.Add(permission);
        }

        await dbContext.SaveChangesAsync();
        return AdminMutationResult.Success("Permission saved.");
    }

    public async Task<AdminMutationResult> DeletePermissionAsync(int id)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var permission = await dbContext.Permissions
            .Include(item => item.RolePermissions)
            .SingleOrDefaultAsync(item => item.Id == id);

        if (permission is null)
        {
            return AdminMutationResult.Failure("Permission not found.");
        }

        if (permission.RolePermissions.Count > 0)
        {
            return AdminMutationResult.Failure("Remove this permission from roles first.");
        }

        dbContext.Permissions.Remove(permission);
        await dbContext.SaveChangesAsync();

        return AdminMutationResult.Success("Permission deleted.");
    }

    public async Task<IReadOnlyList<RoleSummary>> GetRolesAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await dbContext.Roles
            .AsNoTracking()
            .OrderBy(role => role.Name)
            .Select(role => new RoleSummary(
                role.Id,
                role.Name ?? string.Empty,
                role.Description,
                role.RolePermissions.Count,
                dbContext.UserRoles.Count(userRole => userRole.RoleId == role.Id),
                role.Name == AdminPermissionCatalog.AdministratorRoleName))
            .ToListAsync();
    }

    public async Task<IReadOnlySet<int>> GetRolePermissionIdsAsync(string roleId)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await dbContext.RolePermissions
            .AsNoTracking()
            .Where(rolePermission => rolePermission.RoleId == roleId)
            .Select(rolePermission => rolePermission.PermissionId)
            .ToHashSetAsync();
    }

    public async Task<AdminMutationResult> SaveRoleAsync(RoleFormModel model, IReadOnlyCollection<int> permissionIds)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var roleName = model.Name.Trim();

        var duplicate = await roleManager.FindByNameAsync(roleName);

        if (duplicate is not null && duplicate.Id != model.Id)
        {
            return AdminMutationResult.Failure("A role with this name already exists.");
        }

        ApplicationRole? role = null;

        if (!string.IsNullOrWhiteSpace(model.Id))
        {
            role = await roleManager.FindByIdAsync(model.Id);

            if (role is null)
            {
                return AdminMutationResult.Failure("Role not found.");
            }
        }

        IdentityResult result;

        if (role is null)
        {
            role = new ApplicationRole
            {
                Name = roleName,
                Description = NormalizeOptional(model.Description),
            };
            result = await roleManager.CreateAsync(role);
        }
        else
        {
            role.Name = roleName;
            role.Description = NormalizeOptional(model.Description);
            result = await roleManager.UpdateAsync(role);
        }

        if (!result.Succeeded)
        {
            return FromIdentityResult(result);
        }

        await SetRolePermissionsAsync(dbContext, role.Id, permissionIds);
        return AdminMutationResult.Success("Role saved.");
    }

    public async Task<AdminMutationResult> DeleteRoleAsync(string id)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var role = await roleManager.FindByIdAsync(id);

        if (role is null)
        {
            return AdminMutationResult.Failure("Role not found.");
        }

        if (role.Name == AdminPermissionCatalog.AdministratorRoleName)
        {
            return AdminMutationResult.Failure("The administrator role cannot be deleted.");
        }

        var hasUsers = await dbContext.UserRoles.AnyAsync(userRole => userRole.RoleId == id);

        if (hasUsers)
        {
            return AdminMutationResult.Failure("Remove users from this role first.");
        }

        var result = await roleManager.DeleteAsync(role);
        return result.Succeeded
            ? AdminMutationResult.Success("Role deleted.")
            : FromIdentityResult(result);
    }

    public async Task<IReadOnlyList<UserSummary>> GetUsersAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var users = await dbContext.Users
            .AsNoTracking()
            .OrderBy(user => user.Email)
            .ToListAsync();
        var summaries = new List<UserSummary>();

        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            summaries.Add(new UserSummary(
                user.Id,
                user.Email ?? string.Empty,
                user.DisplayName,
                user.IsActive,
                roles.OrderBy(role => role).ToList()));
        }

        return summaries;
    }

    public async Task<IReadOnlySet<string>> GetUserRoleIdsAsync(string userId)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await dbContext.UserRoles
            .AsNoTracking()
            .Where(userRole => userRole.UserId == userId)
            .Select(userRole => userRole.RoleId)
            .ToHashSetAsync();
    }

    public async Task<AdminMutationResult> SaveUserAsync(UserFormModel model, IReadOnlyCollection<string> roleIds)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = model.Email.Trim();
        var duplicate = await userManager.FindByEmailAsync(email);

        if (duplicate is not null && duplicate.Id != model.Id)
        {
            return AdminMutationResult.Failure("A user with this email already exists.");
        }

        ApplicationUser? user = null;
        var isNew = string.IsNullOrWhiteSpace(model.Id);

        if (!isNew)
        {
            user = await userManager.FindByIdAsync(model.Id!);

            if (user is null)
            {
                return AdminMutationResult.Failure("User not found.");
            }
        }

        user ??= new ApplicationUser();
        user.UserName = email;
        user.Email = email;
        user.EmailConfirmed = true;
        user.DisplayName = NormalizeOptional(model.DisplayName);
        user.IsActive = model.IsActive;

        IdentityResult result;

        if (isNew)
        {
            if (string.IsNullOrWhiteSpace(model.Password))
            {
                return AdminMutationResult.Failure("Password is required for new users.");
            }

            result = await userManager.CreateAsync(user, model.Password);
        }
        else
        {
            result = await userManager.UpdateAsync(user);
        }

        if (!result.Succeeded)
        {
            return FromIdentityResult(result);
        }

        if (!isNew && !string.IsNullOrWhiteSpace(model.Password))
        {
            var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
            var resetResult = await userManager.ResetPasswordAsync(user, resetToken, model.Password);

            if (!resetResult.Succeeded)
            {
                return FromIdentityResult(resetResult);
            }
        }

        var roleNames = await dbContext.Roles
            .Where(role => roleIds.Contains(role.Id))
            .Select(role => role.Name!)
            .ToListAsync();

        var currentRoles = await userManager.GetRolesAsync(user);
        var removeResult = await userManager.RemoveFromRolesAsync(user, currentRoles.Except(roleNames));

        if (!removeResult.Succeeded)
        {
            return FromIdentityResult(removeResult);
        }

        var addResult = await userManager.AddToRolesAsync(user, roleNames.Except(currentRoles));

        return addResult.Succeeded
            ? AdminMutationResult.Success("User saved.")
            : FromIdentityResult(addResult);
    }

    public async Task<AdminMutationResult> DeleteUserAsync(string id)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(id);

        if (user is null)
        {
            return AdminMutationResult.Failure("User not found.");
        }

        if (await userManager.IsInRoleAsync(user, AdminPermissionCatalog.AdministratorRoleName))
        {
            var administratorRoleId = await dbContext.Roles
                .Where(role => role.Name == AdminPermissionCatalog.AdministratorRoleName)
                .Select(role => role.Id)
                .SingleAsync();
            var administratorCount = await dbContext.UserRoles.CountAsync(userRole => userRole.RoleId == administratorRoleId);

            if (administratorCount <= 1)
            {
                return AdminMutationResult.Failure("The last administrator cannot be deleted.");
            }
        }

        var result = await userManager.DeleteAsync(user);

        return result.Succeeded
            ? AdminMutationResult.Success("User deleted.")
            : FromIdentityResult(result);
    }

    public async Task<AdminMutationResult> CreateBootstrapAdministratorAsync(UserFormModel model)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (await dbContext.Users.AnyAsync())
        {
            return AdminMutationResult.Failure("Setup is closed because users already exist.");
        }

        if (string.IsNullOrWhiteSpace(model.Password))
        {
            return AdminMutationResult.Failure("Password is required.");
        }

        var user = new ApplicationUser
        {
            UserName = model.Email.Trim(),
            Email = model.Email.Trim(),
            EmailConfirmed = true,
            DisplayName = NormalizeOptional(model.DisplayName),
            IsActive = true,
        };

        var createResult = await userManager.CreateAsync(user, model.Password);

        if (!createResult.Succeeded)
        {
            return FromIdentityResult(createResult);
        }

        var roleResult = await userManager.AddToRoleAsync(user, AdminPermissionCatalog.AdministratorRoleName);

        return roleResult.Succeeded
            ? AdminMutationResult.Success("Administrator created.")
            : FromIdentityResult(roleResult);
    }

    private static async Task SetRolePermissionsAsync(
        ApplicationDbContext dbContext,
        string roleId,
        IReadOnlyCollection<int> permissionIds)
    {
        var existing = await dbContext.RolePermissions
            .Where(rolePermission => rolePermission.RoleId == roleId)
            .ToListAsync();

        dbContext.RolePermissions.RemoveRange(existing);

        foreach (var permissionId in permissionIds.Distinct())
        {
            dbContext.RolePermissions.Add(new ApplicationRolePermission
            {
                RoleId = roleId,
                PermissionId = permissionId,
            });
        }

        await dbContext.SaveChangesAsync();
    }

    private static AdminMutationResult FromIdentityResult(IdentityResult result)
    {
        return AdminMutationResult.Failure(string.Join(" ", result.Errors.Select(error => error.Description)));
    }

    private static string NormalizeKey(string value) => value.Trim().ToLowerInvariant();

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
