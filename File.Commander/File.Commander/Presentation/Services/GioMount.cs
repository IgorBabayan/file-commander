using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace File.Commander.Presentation.Services;

/// <summary>A server asked for a login while mounting.</summary>
/// <param name="Message">The server's own words ("Enter password for share “media” on “nas”").</param>
/// <param name="NeedsUser">It asks for a user name (and the server may let a guest in instead).</param>
/// <param name="NeedsDomain">A Windows share: it may ask for a domain too.</param>
/// <param name="IsRetry">The last login was refused.</param>
public sealed record GioLoginRequest(string Message, bool NeedsUser, bool NeedsDomain, string? DefaultUser,
    string? DefaultDomain, bool IsRetry);

/// <param name="Guest">Log in anonymously instead: the mount is started again as a guest.</param>
public sealed record GioCredentials(string User, string Domain, string Password, bool Guest);

/// <summary>A server asked a question while mounting, e.g. whether to trust an unknown host key.</summary>
public sealed record GioQuestion(string Message, IReadOnlyList<string> Choices);

/// <summary>Answers what a server asks while it is mounted. Called on the thread that started the mount.</summary>
public interface IGioMountHandler
{
    /// <returns>Null: the user cancelled, the mount stops.</returns>
    Task<GioCredentials?> AskLoginAsync(GioLoginRequest request);

    /// <returns>The index of the picked choice; null: the user cancelled, the mount stops.</returns>
    Task<int?> AskQuestionAsync(GioQuestion question);
}

public sealed record GioResult(bool Success, string? Error, bool Cancelled = false)
{
    public static GioResult Ok { get; } = new(true, null);

    public static GioResult WasCancelled { get; } = new(false, null, Cancelled: true);
}

/// <summary>
/// Mounts and unmounts network shares through GVfs with the gio tool, which is how GNOME, Xfce and most
/// file managers on Linux connect to servers. A mounted share is a folder under GVfs's FUSE root,
/// so the rest of the app browses it like any other folder.
/// </summary>
public static partial class GioMount
{
    private const string Gio = "gio";
    private const int MaxLogins = 3;

    // No answer from the server for this long (while no dialog is open): the mount is stopped
    private static readonly TimeSpan MountTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(20);

    // gio asks on stdout and flushes every prompt: "User [igor]: ", "Domain [WORKGROUP]: ", "Password: ", "Choice: "
    [GeneratedRegex(@"^(User|Domain|Password|Choice)(?: \[(.*)\])?: $")]
    private static partial Regex PromptPattern();

    // The choices of a question come before its prompt: "[1] Log In Anyway"
    [GeneratedRegex(@"^\[(\d+)\] (.*)$")]
    private static partial Regex ChoicePattern();

    /// <summary>gio is installed: without it, servers can't be mounted (mounted shares are still listed).</summary>
    public static bool IsAvailable => FindExecutable(Gio) is not null;

    /// <summary>
    /// Mounts <paramref name="uri"/>, asking <paramref name="handler"/> for a login or an answer when the server
    /// wants one. A share that is already mounted counts as a success.
    /// </summary>
    public static async Task<GioResult> MountAsync(string uri, IGioMountHandler handler, CancellationToken token)
    {
        var (result, guest) = await RunMountAsync(uri, guest: false, handler, token);

        // "Connect as guest" in the login dialog: start again, gio answers the server as anonymous
        if (guest)
            (result, _) = await RunMountAsync(uri, guest: true, handler, token);

        return result;
    }

    /// <summary>Unmounts a share, the way it was mounted.</summary>
    public static async Task<GioResult> UnmountAsync(NetworkMount mount, CancellationToken token)
    {
        var (file, arguments) = mount.Kind switch
        {
            NetworkMountKind.Gvfs => (Gio, new[] { "mount", "-u", mount.MountPoint }),
            NetworkMountKind.Fuse => (FindExecutable("fusermount3") is not null ? "fusermount3" : "fusermount",
                new[] { "-u", mount.MountPoint }),
            _ => ("umount", new[] { mount.MountPoint }),
        };

        if (await RunAsync(file, arguments, token) is not { } run)
            return token.IsCancellationRequested ? GioResult.WasCancelled : new GioResult(false, $"“{file}” isn't installed.");

        if (token.IsCancellationRequested)
            return GioResult.WasCancelled;

        return run.ExitCode == 0
            ? GioResult.Ok
            : new GioResult(false, ErrorOf(run.Error) ?? (run.TimedOut ? "The server didn't answer in time." : null));
    }

    /// <summary>The folder a mounted <paramref name="uri"/> is reached through, or null (GVfs's FUSE daemon isn't running).</summary>
    public static async Task<string?> LocalPathAsync(string uri, CancellationToken token)
    {
        if (await RunAsync(Gio, ["info", uri], token) is not { ExitCode: 0 } run)
            return null;

        const string prefix = "local path: ";
        return run.Output
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
    }

    /// <summary>
    /// The shares of a mounted Windows server (smb://nas/), by name. Hidden admin shares (C$, IPC$) are left out.
    /// </summary>
    public static async Task<IReadOnlyList<string>> ListSharesAsync(string uri, CancellationToken token)
    {
        if (await RunAsync(Gio, ["list", uri], token) is not { ExitCode: 0 } run)
            return [];

        return run.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(name => !name.EndsWith('$'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <returns>The result, and whether the user asked to log in as a guest instead.</returns>
    private static async Task<(GioResult Result, bool Guest)> RunMountAsync(string uri, bool guest,
        IGioMountHandler handler, CancellationToken token)
    {
        var arguments = new List<string> { "mount" };
        if (guest)
            arguments.Add("-a");
        arguments.Add(uri);

        using var process = Start(Gio, arguments);
        if (process is null)
            return (new GioResult(false, "gio isn't installed. Install GVfs (the gvfs package) to connect to servers."), false);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(MountTimeout);
        using var kill = timeout.Token.Register(() => Kill(process));

        var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
        var isWindowsShare = uri.StartsWith("smb:", StringComparison.OrdinalIgnoreCase);

        var output = new StringBuilder();
        var buffer = new char[1024];
        var message = string.Empty;
        GioCredentials? credentials = null;
        var passwordSent = false;
        var logins = 0;
        var askGuest = false;
        var stopped = false;
        string? failure = null;

        while (true)
        {
            var read = await process.StandardOutput.ReadAsync(buffer.AsMemory(), CancellationToken.None);
            if (read == 0)
                break;

            output.Append(buffer, 0, read);
            var text = output.ToString();
            var lineStart = text.LastIndexOf('\n') + 1;
            var prompt = PromptPattern().Match(text[lineStart..]);
            if (!prompt.Success)
                continue;

            // What gio printed before the prompt: the server's message, or a question and its choices
            var before = text[..lineStart].Trim();
            output.Clear();

            var kind = prompt.Groups[1].Value;
            var defaultValue = prompt.Groups[2].Success ? prompt.Groups[2].Value : null;
            if (kind != "Choice" && before.Length > 0)
                message = before;

            // The user may take their time in a dialog: only the server's silence counts
            timeout.CancelAfter(Timeout.InfiniteTimeSpan);

            string? answer;
            if (kind == "Choice")
            {
                var (question, choices) = SplitChoices(before);
                var picked = await handler.AskQuestionAsync(new GioQuestion(question, choices));
                answer = picked is { } index ? (index + 1).ToString(CultureInfo.InvariantCulture) : null;
            }
            else
            {
                // A new login round after a password was sent: the server refused it
                var refused = passwordSent && kind != "Domain";
                if (credentials is null || refused)
                {
                    if (++logins > MaxLogins)
                    {
                        failure = "The server refused the user name or password.";
                        answer = null;
                    }
                    else
                    {
                        credentials = await handler.AskLoginAsync(new GioLoginRequest(message,
                            NeedsUser: kind == "User", NeedsDomain: kind == "Domain" || (kind == "User" && isWindowsShare),
                            DefaultUser: kind == "User" ? defaultValue : null,
                            DefaultDomain: kind == "Domain" ? defaultValue : null,
                            IsRetry: refused));
                        passwordSent = false;
                        askGuest = credentials is { Guest: true } && !guest;
                    }
                }

                answer = credentials is null || credentials.Guest || failure is not null
                    ? null
                    : kind switch
                    {
                        "User" => NonEmpty(credentials.User) ?? defaultValue ?? string.Empty,
                        "Domain" => NonEmpty(credentials.Domain) ?? defaultValue ?? string.Empty,
                        _ => credentials.Password,
                    };

                if (kind == "Password")
                    passwordSent = true;
            }

            if (answer is null || token.IsCancellationRequested)
            {
                stopped = true;
                Kill(process);
                break;
            }

            try
            {
                await process.StandardInput.WriteLineAsync(answer);
                await process.StandardInput.FlushAsync(CancellationToken.None);
            }
            catch (IOException)
            {
                // gio is gone: its exit code and errors tell why
                break;
            }

            timeout.CancelAfter(MountTimeout);
        }

        await process.WaitForExitAsync(CancellationToken.None);
        var error = await errors;

        if (token.IsCancellationRequested)
            return (GioResult.WasCancelled, false);
        if (askGuest)
            return (GioResult.WasCancelled, true);
        if (failure is not null)
            return (new GioResult(false, failure), false);
        if (stopped)
            return (GioResult.WasCancelled, false);
        if (timeout.IsCancellationRequested)
            return (new GioResult(false, "The server didn't answer in time."), false);
        if (process.ExitCode == 0)
            return (GioResult.Ok, false);

        var problem = ErrorOf(error);
        return problem is not null && problem.Contains("already mounted", StringComparison.OrdinalIgnoreCase)
            ? (GioResult.Ok, false)
            : (new GioResult(false, problem ?? "The server couldn't be mounted."), false);
    }

    private static (string Question, IReadOnlyList<string> Choices) SplitChoices(string text)
    {
        var question = new List<string>();
        var choices = new List<string>();
        foreach (var line in text.Split('\n'))
        {
            var choice = ChoicePattern().Match(line.TrimEnd());
            if (choice.Success)
                choices.Add(choice.Groups[2].Value);
            else
                question.Add(line);
        }

        return (string.Join('\n', question).Trim(), choices);
    }

    private static async Task<ProcessOutput?> RunAsync(string file, IReadOnlyList<string> arguments, CancellationToken token)
    {
        using var process = Start(file, arguments);
        if (process is null)
            return null;

        // Nothing to answer: a prompt reads end of input and gives up instead of waiting
        process.StandardInput.Close();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(CommandTimeout);
        using var kill = timeout.Token.Register(() => Kill(process));

        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
        await process.WaitForExitAsync(CancellationToken.None);

        return new ProcessOutput(process.ExitCode, await output, await error, timeout.IsCancellationRequested);
    }

    private static Process? Start(string file, IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        // gio's prompts are matched in English; LC_ALL would override LC_MESSAGES, so its charset moves to LC_CTYPE.
        // The server's messages come from the GVfs daemon and stay in the user's language.
        info.Environment.Remove("LANGUAGE");
        if (info.Environment.TryGetValue("LC_ALL", out var all) && !string.IsNullOrEmpty(all))
        {
            info.Environment.Remove("LC_ALL");
            info.Environment["LC_CTYPE"] = all;
        }

        info.Environment["LC_MESSAGES"] = "C";

        try
        {
            return Process.Start(info);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Trace.WriteLine($"Can't run {file}: {ex.Message}");
            return null;
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Already gone
        }
    }

    /// <summary>"gio: smb://nas/media/: Failed to mount Windows share: Connection refused" → what follows the address.</summary>
    private static string? ErrorOf(string error)
    {
        var line = error
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();
        if (line is null)
            return null;

        if (line.StartsWith("gio: ", StringComparison.Ordinal))
            line = line[5..];

        var separator = line.IndexOf(": ", StringComparison.Ordinal);
        if (separator > 0 && (line[..separator].Contains("://", StringComparison.Ordinal) || line[..separator].StartsWith('/')))
            line = line[(separator + 2)..];

        return line.Length > 0 ? line : null;
    }

    private static string? NonEmpty(string value) => value.Length > 0 ? value : null;

    private static string? FindExecutable(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "/usr/local/bin:/usr/bin:/bin";
        return path
            .Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => IOPath.Combine(directory, name))
            .FirstOrDefault(IOFile.Exists);
    }

    private readonly record struct ProcessOutput(int ExitCode, string Output, string Error, bool TimedOut);
}
