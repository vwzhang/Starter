namespace Starter.Web.Data;

public sealed class ApplicationPermission
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int FeatureId { get; set; }
    public ApplicationFeature Feature { get; set; } = default!;
    public ICollection<ApplicationRolePermission> RolePermissions { get; } = [];
}
