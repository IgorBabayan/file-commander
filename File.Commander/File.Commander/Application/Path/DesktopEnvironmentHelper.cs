namespace File.Commander.Application.Path;

static class DesktopEnvironmentHelper
{
    public static bool IsHyprland()
    {
        if (!OperatingSystem.IsLinux())
            return false;

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE")))
            return true;

        var xdgCurrentDesktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP");
        var xdgSessionDesktop = Environment.GetEnvironmentVariable("XDG_SESSION_DESKTOP");

        return (xdgCurrentDesktop?.Contains("Hyprland", StringComparison.OrdinalIgnoreCase) ?? false)
               || (xdgSessionDesktop?.Contains("Hyprland", StringComparison.OrdinalIgnoreCase) ?? false);
    }
}