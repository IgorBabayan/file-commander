using Material.Icons;

namespace File.Commander.Presentation.ViewModels.Helpers;

public static class LocationIcons
{
    /// <summary>Outline glyph shown in the sidebar.</summary>
    public static MaterialIconKind ForSidebar(UserDirectoryKind kind) => kind switch
    {
        UserDirectoryKind.Desktop => MaterialIconKind.DesktopClassic,
        UserDirectoryKind.Videos => MaterialIconKind.Filmstrip,
        UserDirectoryKind.Music => MaterialIconKind.MusicNote,
        UserDirectoryKind.Pictures => MaterialIconKind.ImageOutline,
        UserDirectoryKind.Documents => MaterialIconKind.FileDocumentOutline,
        UserDirectoryKind.Downloads => MaterialIconKind.DownloadCircleOutline,
        _ => MaterialIconKind.FolderOutline,
    };
 
    /// <summary>Glyph drawn in the white badge on the big folder tile.</summary>
    public static MaterialIconKind ForBadge(UserDirectoryKind kind) => kind switch
    {
        UserDirectoryKind.Desktop => MaterialIconKind.Monitor,
        UserDirectoryKind.Videos => MaterialIconKind.Play,
        UserDirectoryKind.Music => MaterialIconKind.MusicNote,
        UserDirectoryKind.Pictures => MaterialIconKind.Image,
        UserDirectoryKind.Documents => MaterialIconKind.FileDocument,
        UserDirectoryKind.Downloads => MaterialIconKind.ArrowDown,
        _ => MaterialIconKind.Folder,
    };
 
    /// <summary>A network share or server, by how it is reached.</summary>
    public static MaterialIconKind ForNetwork(NetworkProtocol protocol) => protocol switch
    {
        NetworkProtocol.Smb => MaterialIconKind.FolderNetworkOutline,
        NetworkProtocol.Sftp => MaterialIconKind.Console,
        NetworkProtocol.WebDav => MaterialIconKind.Web,
        NetworkProtocol.Ftp or NetworkProtocol.Nfs => MaterialIconKind.ServerNetwork,
        _ => MaterialIconKind.LanConnect,
    };

    public static MaterialIconKind ForSidebar(VolumeKind kind) => kind switch
    {
        VolumeKind.Removable => MaterialIconKind.Usb,
        VolumeKind.Optical => MaterialIconKind.Disc,
        _ => MaterialIconKind.Harddisk,
    };
}