namespace Starter.Web.Security;

public sealed class IdentitySeedOptions
{
    public bool SeedDevelopmentTestUsers { get; set; } = true;
    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }
    public string? AdminDisplayName { get; set; }
}
