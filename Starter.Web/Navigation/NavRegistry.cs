using MudBlazor;
using Starter.Web.Components.Pages.Admin;
using Starter.Web.Components.Pages.Demo;
using Starter.Web.Components.Pages.Dev;

namespace Starter.Web.Navigation;

/// <summary>
/// A single entry in the app-shell rail. Each module owns its own navigation
/// markup (<see cref="NavComponent"/>); the shell only switches between modules.
/// </summary>
public record NavModule(string Title, string Icon, Type NavComponent);

/// <summary>
/// The ordered list of modules shown in the rail. Add a folder under
/// <c>Components/Pages</c>, give it a <c>*Nav.razor</c>, and register it here.
/// </summary>
public sealed class NavRegistry
{
    public IReadOnlyList<NavModule> Modules { get; } =
    [
        new("Demo", Icons.Material.Filled.Widgets, typeof(DemoNav)),
        new("Admin", Icons.Material.Filled.AdminPanelSettings, typeof(AdminNav)),
        new("Dev", Icons.Material.Filled.Code, typeof(DevNav)),
    ];
}
