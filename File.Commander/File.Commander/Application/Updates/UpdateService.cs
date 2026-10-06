using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace File.Commander.Application.Updates;

/// <summary>A published release that is newer than the running app, with the AppImage built for this machine.</summary>
/// <param name="Sha256">Hex digest GitHub computed for the asset, or null when the release doesn't list one.</param>
public sealed record AppRelease(Version Version, string Tag, string AssetName, Uri DownloadUrl, long Size, string? Sha256)
{
    /// <summary>"1.0.42".</summary>
    public string VersionText => Version.ToString(3);
}

/// <summary>
/// Updates File Commander from the latest GitHub release. Only the AppImage can update itself: it is replaced in
/// place, under the same name, so launchers and the .desktop file keep pointing at it.
/// </summary>
public interface IUpdateService
{
    /// <summary>Major.minor.build of the running app. 0.0.0 for a local build.</summary>
    Version CurrentVersion { get; }

    /// <summary>"1.0.42", or "0.0.0-dev" for a local build.</summary>
    string CurrentVersionText { get; }

    /// <summary>Runs from an AppImage ($APPIMAGE), so it can be replaced by a newer one.</summary>
    bool CanInstall { get; }

    /// <summary>The latest release when it is newer than this app and has an AppImage for this machine; otherwise null.</summary>
    Task<AppRelease?> FindUpdateAsync(CancellationToken cancellationToken);

    /// <summary>Downloads the release's AppImage next to the running one. Returns the downloaded file.</summary>
    Task<string> DownloadAsync(AppRelease release, IOperationProgress progress);

    /// <summary>Replaces the running AppImage with the downloaded one. The running app goes on until it restarts.</summary>
    void Install(string downloadedPath, IOperationProgress progress);

    /// <summary>Starts the (just installed) AppImage as a new process. The caller then shuts this one down.</summary>
    void StartNewInstance();
}

internal sealed class UpdateService : IUpdateService, IDisposable
{
    /// <summary>The GitHub repository whose releases carry the AppImage (.github/workflows/release.yml).</summary>
    public const string Repository = "IgorBabayan/file-commander";

    private static readonly Uri LatestReleaseUri = new($"https://api.github.com/repos/{Repository}/releases/latest");
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(30);

    // Set by the AppImage runtime for the process it starts; a new instance gets its own
    private static readonly string[] AppImageVariables = ["APPIMAGE", "APPDIR", "ARGV0", "OWD"];

    private readonly HttpClient _http;

    public UpdateService()
    {
        var assembly = typeof(UpdateService).Assembly;
        CurrentVersion = Normalize(assembly.GetName().Version ?? new Version(0, 0, 0));

        // The SDK appends "+<commit>" to the informational version
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        CurrentVersionText = string.IsNullOrWhiteSpace(informational)
            ? CurrentVersion.ToString(3)
            : informational.Split('+')[0];

        var appImage = Environment.GetEnvironmentVariable("APPIMAGE");
        AppImagePath = OperatingSystem.IsLinux() && !string.IsNullOrWhiteSpace(appImage) && IOFile.Exists(appImage)
            ? appImage
            : null;

        // Large enough for a slow connection; Cancel in the Action center stops it sooner
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("file-commander", CurrentVersion.ToString(3)));
    }

    public Version CurrentVersion { get; }

    public string CurrentVersionText { get; }

    public bool CanInstall => AppImagePath is not null;

    private string? AppImagePath { get; }

    public async Task<AppRelease?> FindUpdateAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CheckTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await _http.SendAsync(request, timeout.Token);

        // Nothing is published yet
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
        var root = json.RootElement;

        var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
        if (tag is null || !TryParseVersion(tag, out var version))
        {
            Trace.WriteLine($"Latest release has a tag that isn't a version: '{tag}'");
            return null;
        }

        if (version <= CurrentVersion)
            return null;

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        var architecture = ArchitectureTag();
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            var url = asset.TryGetProperty("browser_download_url", out var urlElement) ? urlElement.GetString() : null;
            if (name is null || url is null
                || !name.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase)
                || !name.Contains(architecture, StringComparison.OrdinalIgnoreCase))
                continue;

            var size = asset.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var bytes)
                ? bytes
                : -1;

            // GitHub lists "sha256:<hex>" for assets uploaded since mid-2025
            string? sha256 = null;
            if (asset.TryGetProperty("digest", out var digest) && digest.ValueKind == JsonValueKind.String
                && digest.GetString() is { } value && value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                sha256 = value["sha256:".Length..];

            return new AppRelease(version, tag, name, new Uri(url), size, sha256);
        }

        Trace.WriteLine($"Release {tag} has no AppImage for {architecture}");
        return null;
    }

    public async Task<string> DownloadAsync(AppRelease release, IOperationProgress progress)
    {
        var target = AppImagePath ?? throw new InvalidOperationException("File Commander isn't running from an AppImage.");
        var cancellationToken = progress.CancellationToken;

        // Same folder as the AppImage: the move that installs it is then a rename, which can't be seen half done
        var folder = IOPath.GetDirectoryName(target)!;
        var tempPath = IOPath.Combine(folder, $".{IOPath.GetFileName(target)}.download");

        progress.SetTotal(1, release.Size > 0 ? release.Size : -1);
        progress.Begin(release.AssetName);

        try
        {
            using var response = await _http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            if (release.Size <= 0 && response.Content.Headers.ContentLength is long length and > 0)
                progress.SetTotal(1, length);

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var file = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                             bufferSize: 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    hash.AppendData(buffer, 0, read);
                    progress.Advance(0, read);
                }
            }

            if (release.Sha256 is { } expected)
            {
                var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The download is damaged: its checksum doesn't match the release.");
            }

            progress.Advance();
            return tempPath;
        }
        catch (UnauthorizedAccessException ex)
        {
            TryDelete(tempPath);
            throw new UnauthorizedAccessException(
                $"Can't write to {folder}. Move the AppImage to a folder you can write to, or update it by hand.", ex);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    public void Install(string downloadedPath, IOperationProgress progress)
    {
        var target = AppImagePath ?? throw new InvalidOperationException("File Commander isn't running from an AppImage.");

        progress.SetTotal(1);
        progress.Begin(IOPath.GetFileName(target));

        try
        {
            progress.CancellationToken.ThrowIfCancellationRequested();

            if (OperatingSystem.IsLinux())
            {
                IOFile.SetUnixFileMode(downloadedPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            // A rename: the running app keeps its (now unlinked) image mounted until it exits
            IOFile.Move(downloadedPath, target, overwrite: true);
            progress.Advance();
        }
        catch
        {
            TryDelete(downloadedPath);
            throw;
        }
    }

    public void StartNewInstance()
    {
        var target = AppImagePath ?? throw new InvalidOperationException("File Commander isn't running from an AppImage.");

        var startInfo = new ProcessStartInfo(target)
        {
            UseShellExecute = false,
            // The folder the AppImage was started from, not its mount point
            WorkingDirectory = Environment.GetEnvironmentVariable("OWD") is { Length: > 0 } owd && Directory.Exists(owd)
                ? owd
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };

        foreach (var argument in Environment.GetCommandLineArgs().Skip(1))
            startInfo.ArgumentList.Add(argument);

        foreach (var variable in AppImageVariables)
            startInfo.Environment.Remove(variable);

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("The updated File Commander didn't start.");
    }

    public void Dispose() => _http.Dispose();

    /// <summary>"v1.0.42", "1.0.42-beta" or "1.0" → 1.0.42 / 1.0.0.</summary>
    internal static bool TryParseVersion(string text, out Version version)
    {
        var value = text.Trim().TrimStart('v', 'V');
        var end = value.IndexOfAny(['-', '+']);
        if (end >= 0)
            value = value[..end];

        if (Version.TryParse(value, out var parsed))
        {
            version = Normalize(parsed);
            return true;
        }

        version = new Version(0, 0, 0);
        return false;
    }

    // 1.0.42.0 (an assembly version) and 1.0.42 (a tag) must compare equal: Version ranks a missing part below 0
    private static Version Normalize(Version version)
        => new(version.Major, version.Minor, Math.Max(version.Build, 0));

    private static string ArchitectureTag() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.Arm64 => "aarch64",
        _ => "x86_64",
    };

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
