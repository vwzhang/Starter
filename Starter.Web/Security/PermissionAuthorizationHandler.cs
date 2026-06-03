using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Starter.Web.Data;

namespace Starter.Web.Security;

public sealed class PermissionAuthorizationHandler(IServiceScopeFactory scopeFactory)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var hasPermission = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId && user.IsActive)
            .Join(dbContext.UserRoles, user => user.Id, userRole => userRole.UserId, (_, userRole) => userRole.RoleId)
            .Join(dbContext.RolePermissions, roleId => roleId, rolePermission => rolePermission.RoleId, (_, rolePermission) => rolePermission)
            .AnyAsync(rolePermission =>
                rolePermission.Permission.Key == requirement.PermissionKey
                && rolePermission.Permission.Feature.IsEnabled);

        if (hasPermission)
        {
            context.Succeed(requirement);
        }
    }
}
