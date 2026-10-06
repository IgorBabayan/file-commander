using System.Globalization;
using System.Text;
using Avalonia.Platform;

namespace File.Commander.Presentation.Services;

public interface IDesktopService
{
    void BuildDesktopFile();
    Task SaveDesktopFileAsync(CancellationToken cancellationToken);
    bool HasDesktopFile { get; }
}

class DesktopService : IDesktopService
{
    private const string ICON_NAME = "file-commander";
    private const string DESKTOP_FILE_NAME = "file-commander.desktop";
    
    private readonly StringBuilder _builder = new();

    public bool HasDesktopFile
    {
        get
        {
            var directory = GetApplicationsDirectory();
            var path = IOPath.Combine(directory, DESKTOP_FILE_NAME);
            return IOFile.Exists(path);
        }
    }
    
    public void BuildDesktopFile()
    {
        _builder.Clear();
        _builder.AppendLine("[Desktop Entry]")
            .AppendLine("Type=Application")
            .AppendLine("Categories=Utility;")
            .AppendLine("Comment=Modern file commander")
            .AppendLine("Terminal=false")
            .AppendLine("Name=File Commander")
            .AppendLine("StartupWMClass=file-commander")
            .AppendLine(CultureInfo.InvariantCulture, $"Exec={QuoteExecArgument(GetAppPath())}")
            .AppendLine(CultureInfo.InvariantCulture, $"Icon={GetAppIcon()}");
    }
    
    public async Task SaveDesktopFileAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        var directory = GetApplicationsDirectory();
        Directory.CreateDirectory(directory);

        var path = IOPath.Combine(directory, DESKTOP_FILE_NAME);
        var tempPath = path + ".tmp";

        await IOFile.WriteAllTextAsync(tempPath, _builder.ToString(), new UTF8Encoding(false), cancellationToken);
        IOFile.Move(tempPath, path, overwrite: true);

        if (OperatingSystem.IsLinux())
        {
            IOFile.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }
    
    private static string GetAppIcon()
    {
        var dataHome = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var targetDir = IOPath.Combine(dataHome, "icons", "hicolor", "256x256", "apps");
        Directory.CreateDirectory(targetDir);

        var targetPath = IOPath.Combine(targetDir, ICON_NAME + ".png");

        using var source = AssetLoader.Open(new Uri("avares://File.Commander/Assets/logo.png"));
        using var target = IOFile.Create(targetPath);
        source.CopyTo(target);

        return ICON_NAME;
    }
    
    private static string GetAppPath() => Environment.GetEnvironmentVariable("APPIMAGE") ?? Environment.ProcessPath
        ?? throw new InvalidOperationException("Cannot determine the application executable path.");
    
    private static string GetApplicationsDirectory()
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(dataHome))
        {
            dataHome = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "share");
        }

        return IOPath.Combine(dataHome, "applications");
    }
    
    private static string QuoteExecArgument(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');

        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                case '`':
                case '$':
                    builder.Append('\\').Append(c);
                    break;
                case '\\':
                    builder.Append(@"\\\\");
                    break;
                case '%':
                    builder.Append("%%");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.Append('"').ToString();
    }
}