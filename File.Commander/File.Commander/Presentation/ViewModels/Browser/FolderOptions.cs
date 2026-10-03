namespace File.Commander.Presentation.ViewModels.Browser;

/// <summary>
/// How folder pages read and show their entries. Fixed for a page: the shell rebuilds the page when it changes.
/// </summary>
/// <param name="ShowHidden">List dot files.</param>
/// <param name="ShowExtensions">False: "photo.png" is shown as "photo".</param>
/// <param name="MixFilesAndFolders">False: folders come first, whatever the order.</param>
/// <param name="OpenOnSingleClick">A click opens an entry instead of a double click.</param>
public sealed record FolderOptions(bool ShowHidden, bool ShowExtensions, bool MixFilesAndFolders, bool OpenOnSingleClick);
