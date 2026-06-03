namespace Starter.Web.Data;

public sealed class ApplicationRolePermission
{
    public string RoleId { get; set; } = string.Empty;
    public int PermissionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ApplicationRole Role { get; set; } = default!;
    public ApplicationPermission Permission { get; set; } = default!;
}
