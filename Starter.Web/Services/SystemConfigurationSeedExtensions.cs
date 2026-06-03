namespace Starter.Web.Services;

public static class SystemConfigurationSeedExtensions
{
    public static async Task InitializeSystemConfigurationAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var systemConfiguration = services.GetRequiredService<SystemConfigurationService>();
        await systemConfiguration.EnsureDefaultsAsync(cancellationToken);
    }
}
