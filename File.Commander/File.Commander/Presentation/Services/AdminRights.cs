using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace File.Commander.Presentation.Services;

/// <summary>Something done with administrator rights: one item of an action, as shell commands.</summary>
/// <param name="Item">The item's name, reported to the Action center when it fails.</param>
/// <param name="Script">
/// POSIX shell commands, built with <see cref="AdminRights"/>' helpers. A non-zero exit status is a failure, and what
/// they wrote to stderr is the reason.
/// </param>
/// <param name="Items">Items the Action center counts as done once it ran.</param>
/// <param name="Bytes">Bytes the Action center counts as done once it ran.</param>
/// <param name="Group">
/// Steps that depend on each other (the parts of one item copied as root, then the moved source deleted): once one of
/// a group fails, the later ones of that group are skipped. -1: depends on nothing.
/// </param>
public sealed record AdminStep(string Item, string Script, long Items = 1, long Bytes = 0, int Group = -1);

/// <summary>How a run with administrator rights ended.</summary>
public enum AdminOutcome
{
    /// <summary>Authorized and run. Each step may still have failed on its own.</summary>
    Done,

    /// <summary>The authentication dialog was dismissed, or the action canceled while it was open.</summary>
    Canceled,

    /// <summary>Not authorized (wrong password, no authentication agent…), or pkexec couldn't run.</summary>
    Denied,
}

/// <param name="Failures">The steps that failed, by their index, with what went wrong.</param>
/// <param name="Problem">Why nothing ran, when <see cref="Outcome"/> isn't <see cref="AdminOutcome.Done"/>.</param>
public sealed record AdminRunResult(AdminOutcome Outcome, IReadOnlyDictionary<int, string> Failures, string? Problem);

/// <summary>
/// Does what the user isn't allowed to do with their own rights (writing to /usr, renaming a file owned by root…) with
/// administrator rights, asking for them in the system's own authentication dialog: polkit's, through pkexec, which every
/// desktop's authentication agent answers (GNOME Shell, KDE's polkit agent, polkit-gnome, hyprpolkitagent…).
/// </summary>
/// <remarks>
/// <para>
/// An action collects the items that were refused (an <see cref="UnauthorizedAccessException"/>) and hands them over
/// together at its end: pkexec asks once per run, so a paste of a hundred files into /opt asks once, not a hundred times.
/// One run at a time: two actions refused at once ask one after the other, never with two dialogs.
/// </para>
/// <para>
/// The commands are coreutils (cp, mv, rm, mkdir, chown), run by /bin/sh as root from a script file only the user can
/// write. What is created as root is given to the owner of the folder it is created in, as an item created there by
/// its owner would be: copied into /usr it belongs to root, into another user's folder to that user.
/// </para>
/// </remarks>
public static class AdminRights
{
    // pkexec: the dialog was dismissed; not authorized, or no authentication agent
    private const int DismissedExitCode = 126;
    private const int NotAuthorizedExitCode = 127;

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Lazy<string?> Pkexec = new(() => Terminals.FindProgram("pkexec"));

    // errno values .NET puts in an IOException's HResult on Unix
    private const int PermissionDenied = 13; // EACCES
    private const int NotPermitted = 1;      // EPERM

    /// <summary>
    /// The user isn't allowed to do it, but root would be. .NET reports that as an <see cref="UnauthorizedAccessException"/>
    /// for files, but as a plain <see cref="IOException"/> carrying the errno for some calls (renaming or deleting a
    /// folder): both count.
    /// </summary>
    public static bool IsDenied(Exception exception)
        => exception is UnauthorizedAccessException
           || exception is IOException { HResult: PermissionDenied or NotPermitted };

    /// <summary>Administrator rights can be asked for: pkexec (polkit) is installed.</summary>
    public static bool IsAvailable => OperatingSystem.IsLinux() && Pkexec.Value is not null;

    // ===== Commands =====

    /// <summary>A word for the shell: single-quoted, so nothing in it is expanded.</summary>
    public static string Quote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    /// <summary>Renames or moves <paramref name="source"/> to <paramref name="destination"/> (a path, never into it).</summary>
    public static string Move(string source, string destination)
        => $"mv -T -- {Quote(source)} {Quote(destination)}";

    /// <summary>
    /// Copies <paramref name="source"/> to <paramref name="destination"/> as FileOperations does: links as links, the
    /// mode and times kept. What is created belongs to the owner of <paramref name="ownerFolder"/>. A folder that is
    /// already at <paramref name="destination"/> gets the source's contents.
    /// </summary>
    public static string Copy(string source, string destination, string ownerFolder)
        => $"cp -R -P --preserve=mode,timestamps -T -- {Quote(source)} {Quote(destination)} && {GiveTo(destination, ownerFolder)}";

    /// <summary>Deletes <paramref name="path"/> and everything in it. A link is deleted, never followed.</summary>
    public static string Delete(string path) => $"rm -rf -- {Quote(path)}";

    /// <summary>An empty folder, belonging to the owner of the folder it is in.</summary>
    public static string CreateFolder(string path)
        => $"mkdir -- {Quote(path)} && {GiveTo(path, FileOperations.ParentOf(path))}";

    /// <summary>An empty file, belonging to the owner of the folder it is in. Never truncates one that is there.</summary>
    public static string CreateFile(string path)
        => $"(set -C; : > {Quote(path)}) && {GiveTo(path, FileOperations.ParentOf(path))}";

    /// <summary>
    /// Moves <paramref name="staged"/>, made by the user where they can write, to <paramref name="destination"/>, where
    /// they can't; it then belongs to the owner of <paramref name="ownerFolder"/>. Fails when the name was taken meanwhile.
    /// </summary>
    public static string Place(string staged, string destination, string ownerFolder)
        => $"if [ -e {Quote(destination)} ] || [ -L {Quote(destination)} ]; then "
           + $"echo {Quote($"An item named “{IOPath.GetFileName(destination)}” already exists.")} >&2; false; "
           + $"else {Move(staged, destination)} && {GiveTo(destination, ownerFolder)}; fi";

    /// <summary>
    /// Puts <paramref name="source"/> in place of <paramref name="destination"/>, as FileOperations' replace does: it
    /// arrives under the hidden name <paramref name="staging"/> first, and only once all of it is there is the old item
    /// deleted. On a failure the old item stays, and a renamed source goes back where it was.
    /// </summary>
    public static string Replace(string source, string destination, string staging, bool rename)
    {
        if (rename)
        {
            return $"{Move(source, staging)} && {{ {{ {Delete(destination)} && {Move(staging, destination)}; }} "
                   + $"|| {{ {Move(staging, source)}; false; }}; }}";
        }

        var folder = FileOperations.ParentOf(destination);
        return $"{{ {Copy(source, staging, folder)} && {Delete(destination)} && {Move(staging, destination)}; }} "
               + $"|| {{ {Delete(staging)}; false; }}";
    }

    /// <summary>
    /// Moves <paramref name="path"/> into a trash's files/ folder as <paramref name="stored"/>; its info file was written
    /// by the user and goes when the move fails. The item then belongs to the trash's owner, the user, who can restore
    /// it or empty the trash without asking again.
    /// </summary>
    public static string MoveToTrash(string path, string stored, string infoFile)
        => $"{{ {Move(path, stored)} && {GiveTo(stored, FileOperations.ParentOf(stored))}; }} "
           + $"|| {{ rm -f -- {Quote(infoFile)}; false; }}";

    /// <summary>
    /// Gives <paramref name="path"/> (and everything in it) to the owner of <paramref name="folder"/>. Never fails the
    /// step: a drive without owners (FAT, exFAT) can't take it, and the item is there anyway.
    /// </summary>
    private static string GiveTo(string path, string folder)
        => $"{{ chown -R -h --reference={Quote(folder)} -- {Quote(path)} 2>/dev/null || :; }}";

    /// <summary>
    /// A path the user can write to, for what is made before it is moved with <see cref="Place"/>: in the cache folder,
    /// on disk rather than in a /tmp held in memory, as archives can be large. Its folder is created; the path isn't.
    /// </summary>
    public static string StagingPath(string name)
    {
        var configured = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var cache = !string.IsNullOrEmpty(configured) && IOPath.IsPathRooted(configured)
            ? configured
            : IOPath.Combine(SystemLocations.HomeDirectory, ".cache");

        var folder = IOPath.Combine(cache, "file-commander", "staging");
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(folder);
        else
            Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        return IOPath.Combine(folder, $"{Guid.NewGuid():N}-{name}");
    }

    // ===== Running =====

    /// <summary>
    /// Runs <paramref name="steps"/> as an action's last part, reporting to its <paramref name="progress"/>: each step
    /// that fails, or all of them when the rights weren't given, is listed as failed; each is counted as done.
    /// For actions in the Action center, which run off the UI thread.
    /// </summary>
    public static void RunInto(IReadOnlyList<AdminStep> steps, IOperationProgress progress)
    {
        if (steps.Count == 0 || progress.CancellationToken.IsCancellationRequested)
            return;

        progress.Begin(steps.Count == 1
            ? $"{steps[0].Item} (waiting for administrator rights)"
            : $"{steps.Count:N0} items (waiting for administrator rights)");

        var result = RunAsync(steps, progress.CancellationToken).GetAwaiter().GetResult();
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (result.Outcome != AdminOutcome.Done)
                progress.Fail(step.Item, result.Problem ?? "Administrator rights weren't given");
            else if (result.Failures.TryGetValue(i, out var failure))
                progress.Fail(step.Item, failure);

            progress.Advance(step.Items, step.Bytes);
        }
    }

    /// <summary>
    /// One thing done with administrator rights, for the actions that don't run in the Action center (Rename, New
    /// folder…). Returns what went wrong, or null: also when the user dismissed the dialog, as they chose not to.
    /// </summary>
    public static async Task<string?> RunOneAsync(string item, string script)
    {
        var result = await RunAsync([new AdminStep(item, script)], CancellationToken.None);
        return result.Outcome switch
        {
            AdminOutcome.Canceled => null,
            AdminOutcome.Denied => result.Problem,
            _ => result.Failures.TryGetValue(0, out var failure) ? failure : null,
        };
    }

    /// <summary>
    /// Asks for administrator rights in the system's dialog, then runs every step. Canceling closes the dialog when it
    /// is still open; once the steps run as root, they finish.
    /// </summary>
    public static async Task<AdminRunResult> RunAsync(IReadOnlyList<AdminStep> steps, CancellationToken cancellationToken)
    {
        var none = new Dictionary<int, string>();
        if (steps.Count == 0)
            return new AdminRunResult(AdminOutcome.Done, none, null);

        if (!IsAvailable)
        {
            return new AdminRunResult(AdminOutcome.Denied, none,
                "Administrator rights are needed, and they can't be asked for: pkexec (polkit) isn't installed.");
        }

        try
        {
            await Gate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new AdminRunResult(AdminOutcome.Canceled, none, "Canceled");
        }

        string? scriptPath = null;
        try
        {
            scriptPath = WriteScript(steps);
            return await RunScriptAsync(scriptPath, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception
                                       or InvalidOperationException)
        {
            Trace.WriteLine($"Can't run with administrator rights: {ex.Message}");
            return new AdminRunResult(AdminOutcome.Denied, none, $"Administrator rights couldn't be asked for: {ex.Message}");
        }
        finally
        {
            if (scriptPath is not null)
                TryDelete(scriptPath);

            Gate.Release();
        }
    }

    private static async Task<AdminRunResult> RunScriptAsync(string scriptPath, CancellationToken cancellationToken)
    {
        // pkexec clears the environment and runs the program as root; the authentication agent of the session
        // shows the dialog
        var start = new ProcessStartInfo(Pkexec.Value!)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("/bin/sh");
        start.ArgumentList.Add(scriptPath);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("pkexec didn't start.");
        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);

        // While the dialog is open pkexec still runs as the user, so it can be stopped; the script, run as root, can't
        using (cancellationToken.Register(() => TryKill(process)))
            await process.WaitForExitAsync(CancellationToken.None);

        var stdout = await output;
        var stderr = (await errors).Trim();
        var none = new Dictionary<int, string>();

        if (cancellationToken.IsCancellationRequested && process.ExitCode != 0)
            return new AdminRunResult(AdminOutcome.Canceled, none, "Canceled");

        switch (process.ExitCode)
        {
            case 0:
                return new AdminRunResult(AdminOutcome.Done, ParseFailures(stdout), null);

            case DismissedExitCode:
                return new AdminRunResult(AdminOutcome.Canceled, none, "Administrator rights weren't given");

            case NotAuthorizedExitCode:
                Trace.WriteLine($"pkexec: not authorized: {stderr}");
                return new AdminRunResult(AdminOutcome.Denied, none, stderr.Contains("authentication agent",
                        StringComparison.OrdinalIgnoreCase)
                    ? "Administrator rights are needed, and no authentication agent is running to ask for them."
                    : "Administrator rights weren't given.");

            default:
                Trace.WriteLine($"pkexec exited with {process.ExitCode}: {stderr}");
                return new AdminRunResult(AdminOutcome.Denied, none,
                    stderr.Length > 0 ? FirstLine(stderr) : $"It ended with error {process.ExitCode}.");
        }
    }

    /// <summary>
    /// The script: each step in turn, in a subshell whose output is kept; a step that fails prints "index TAB reason"
    /// (on one line) to stdout. It always ends with 0, so pkexec's own exit codes stand out.
    /// </summary>
    private static string WriteScript(IReadOnlyList<AdminStep> steps)
    {
        var script = new StringBuilder();
        script.Append("#!/bin/sh\n")
            .Append("fc_report() { printf '%s\\t%s\\n' \"$1\" \"$(printf '%s' \"${2:-Failed}\" | tr '\\n\\t' '  ')\"; }\n");

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var group = step.Group >= 0
                ? "fc_failed_g" + step.Group.ToString(CultureInfo.InvariantCulture)
                : "fc_failed_s" + i.ToString(CultureInfo.InvariantCulture);
            var index = i.ToString(CultureInfo.InvariantCulture);

            script.Append(CultureInfo.InvariantCulture, $"if [ -n \"${{{group}:-}}\" ]; then\n")
                .Append(CultureInfo.InvariantCulture, $"  fc_report {index} 'Skipped, as an earlier part of it failed'\n")
                .Append("elif ! fc_out=$( (\n")
                .Append(step.Script).Append('\n')
                .Append(") 2>&1 ); then\n")
                .Append(CultureInfo.InvariantCulture, $"  fc_report {index} \"$fc_out\"\n")
                .Append(CultureInfo.InvariantCulture, $"  {group}=1\n")
                .Append("fi\n");
        }

        script.Append("exit 0\n");

        var path = IOPath.Combine(IOPath.GetTempPath(), $"file-commander-admin-{Guid.NewGuid():N}.sh");
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        using (var stream = new FileStream(path, options))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            writer.Write(script.ToString());

        return path;
    }

    private static Dictionary<int, string> ParseFailures(string output)
    {
        var failures = new Dictionary<int, string>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = line.IndexOf('\t');
            if (tab > 0 && int.TryParse(line.AsSpan(0, tab), NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            {
                var reason = line[(tab + 1)..].Trim();
                failures[index] = reason.Length > 0 ? reason : "Failed";
            }
        }

        return failures;
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
            if (!process.HasExited)
                process.Kill();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            // Already running as root, or gone
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            IOFile.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Can't delete '{path}': {ex.Message}");
        }
    }
}
