using Microsoft.AspNetCore.Identity;

namespace Starter.Web.Data;

public sealed class ApplicationRole : IdentityRole
{
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<ApplicationRolePermission> RolePermissions { get; } = [];
}
