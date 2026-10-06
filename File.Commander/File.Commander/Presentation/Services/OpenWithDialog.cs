using System.ComponentModel;

namespace File.Commander.Presentation.Services;

/// <summary>
/// The system's "Open with" dialog, as other file managers show it: the app chooser of xdg-desktop-portal, drawn by the
/// desktop's own portal backend (GNOME's, KDE's, the GTK one elsewhere). It lists the apps for the file's type, and can
/// make the one picked the default. The portal then opens the file in it.
/// </summary>
/// <remarks>
/// The portal's OpenURI.OpenFile with ask=true. It takes the file as a file descriptor (it refuses file:// URIs), which
/// .NET can't send over D-Bus by itself: a shell opens the file as descriptor 3 and hands it to gdbus (GLib), or to
/// busctl (systemd) where gdbus isn't installed. The call returns as soon as the dialog is up.
/// </remarks>
public static class OpenWithDialog
{
    private const string PortalService = "org.freedesktop.portal.Desktop";
    private const string PortalObject = "/org/freedesktop/portal/desktop";
    private const string OpenUriInterface = "org.freedesktop.portal.OpenURI";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    // "$1" is the file, opened read-only as descriptor 3 for the call
    private static readonly (string Program, string Script)[] Callers =
    [
        ("gdbus",
            $"exec 3< \"$1\" && exec gdbus call --session --timeout {(int)Timeout.TotalSeconds} --dest {PortalService} "
            + $"--object-path {PortalObject} --method {OpenUriInterface}.OpenFile \"''\" 3 \"{{'ask': <true>}}\""),
        ("busctl",
            $"exec 3< \"$1\" && exec busctl --user call {PortalService} {PortalObject} {OpenUriInterface} OpenFile "
            + "'sha{sv}' '' 3 1 ask b true"),
    ];

    /// <summary>Shows the dialog for <paramref name="path"/>, a file. Returns what went wrong, or null.</summary>
    public static async Task<string?> ShowAsync(string path)
    {
        if (!IOFile.Exists(path))
            return "It doesn't exist anymore.";

        string? lastProblem = null;
        foreach (var (program, script) in Callers)
        {
            if (Terminals.FindProgram(program) is null)
                continue;

            var (exitCode, errors) = await RunAsync(script, path);
            if (exitCode == 0)
                return null;

            Trace.WriteLine($"Open with: {program} failed ({exitCode}): {errors}");
            lastProblem = errors;
        }

        if (lastProblem is null)
        {
            return "The “Open with” dialog needs xdg-desktop-portal, and gdbus (from GLib) or busctl (from systemd) "
                   + "to reach it. Neither of the two is installed.";
        }

        return lastProblem.Contains("Permission denied", StringComparison.OrdinalIgnoreCase)
            ? "You aren't allowed to read this file."
            : "The system's “Open with” dialog couldn't be shown. Check that xdg-desktop-portal and a portal backend "
              + $"for your desktop are installed.\n\n{FirstLine(lastProblem)}";
    }

    private static async Task<(int ExitCode, string Errors)> RunAsync(string script, string path)
    {
        var start = new ProcessStartInfo("/bin/sh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(script);
        start.ArgumentList.Add("sh");
        start.ArgumentList.Add(path);

        try
        {
            using var process = Process.Start(start);
            if (process is null)
                return (-1, "The shell didn't start.");

            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();

            // gdbus has its own timeout; this one only guards against a call that never returns
            using var timeout = new CancellationTokenSource(Timeout + TimeSpan.FromSeconds(5));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                return (-1, "The portal didn't answer.");
            }

            await output;
            return (process.ExitCode, (await errors).Trim());
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return (-1, ex.Message);
        }
    }

    private static string FirstLine(string text)
    {
        var newline = text.IndexOf('\n');
        return newline < 0 ? text : text[..newline].Trim();
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            // Gone already
        }
    }
}
