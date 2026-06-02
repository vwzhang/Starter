namespace Starter.Web.Services;

/// <summary>
/// Holds app-shell UI state that should survive page/layout switches within a
/// circuit: which rail module is active, whether the drawer is open, and the
/// current theme mode. Registered as Scoped (one per Blazor circuit).
/// </summary>
public sealed class ShellStateService
{
    public bool DrawerOpen { get; set; } = true;
    public bool IsDarkMode { get; set; }
    public int ActiveModuleIndex { get; set; }
}
