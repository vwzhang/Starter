namespace Starter.Web.Security;

public static class AdminPermissionCatalog
{
    public const string AdministratorRoleName = "Administrator";
    public const string ManagerRoleName = "Manager";
    public const string UserRoleName = "User";

    public const string AccessAdmin = "admin.access.view";
    public const string ManageUsers = "admin.users.manage";
    public const string ManageRoles = "admin.roles.manage";
    public const string ManagePermissions = "admin.permissions.manage";
    public const string ManageFeatures = "admin.features.manage";
    public const string ManageSystem = "admin.system.manage";

    public static readonly FeatureSeed[] Features =
    [
        new("admin.access", "Admin access", "Access to the administration module."),
        new("admin.users", "Users", "User lifecycle and account status."),
        new("admin.roles", "Roles", "Role membership and permission assignment."),
        new("admin.permissions", "Permissions", "Permission definitions."),
        new("admin.features", "Features", "Feature definitions and enablement."),
        new("admin.system", "System", "System configuration and server settings."),
    ];

    public static readonly PermissionSeed[] Permissions =
    [
        new(AccessAdmin, "Access admin", "Open the administration module.", "admin.access"),
        new(ManageUsers, "Manage users", "Create, edit, disable, and delete users.", "admin.users"),
        new(ManageRoles, "Manage roles", "Create roles and assign permissions.", "admin.roles"),
        new(ManagePermissions, "Manage permissions", "Create permissions and attach them to features.", "admin.permissions"),
        new(ManageFeatures, "Manage features", "Create and enable application features.", "admin.features"),
        new(ManageSystem, "Manage system", "Edit system configuration.", "admin.system"),
    ];
}

public sealed record FeatureSeed(string Key, string Name, string Description);

public sealed record PermissionSeed(string Key, string Name, string Description, string FeatureKey);
