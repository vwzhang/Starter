using System.ComponentModel.DataAnnotations;

namespace Starter.Web.Services;

public sealed record AdminMutationResult(bool Succeeded, string Message)
{
    public static AdminMutationResult Success(string message) => new(true, message);
    public static AdminMutationResult Failure(string message) => new(false, message);
}

public sealed record AdminDashboardSummary(int UserCount, int RoleCount, int PermissionCount, int FeatureCount);

public sealed record FeatureSummary(
    int Id,
    string Key,
    string Name,
    string? Description,
    bool IsEnabled,
    int PermissionCount);

public sealed record PermissionSummary(
    int Id,
    string Key,
    string Name,
    string? Description,
    int FeatureId,
    string FeatureName,
    string FeatureKey,
    int RoleCount);

public sealed record RoleSummary(
    string Id,
    string Name,
    string? Description,
    int PermissionCount,
    int UserCount,
    bool IsAdministrator);

public sealed record UserSummary(
    string Id,
    string Email,
    string? DisplayName,
    bool IsActive,
    IReadOnlyList<string> Roles);

public sealed class FeatureFormModel
{
    public int? Id { get; set; }

    [Required]
    [StringLength(120)]
    public string Key { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(512)]
    public string? Description { get; set; }

    public bool IsEnabled { get; set; } = true;
}

public sealed class PermissionFormModel
{
    public int? Id { get; set; }

    [Required]
    [StringLength(160)]
    public string Key { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(512)]
    public string? Description { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Select a feature.")]
    public int FeatureId { get; set; }
}

public sealed class RoleFormModel
{
    public string? Id { get; set; }

    [Required]
    [StringLength(256)]
    public string Name { get; set; } = string.Empty;

    [StringLength(512)]
    public string? Description { get; set; }
}

public sealed class UserFormModel
{
    public string? Id { get; set; }

    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [StringLength(200)]
    public string? DisplayName { get; set; }

    [StringLength(128, MinimumLength = 6)]
    public string? Password { get; set; }

    public bool IsActive { get; set; } = true;
}
