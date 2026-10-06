namespace File.Commander.Presentation.ViewModels.Shell;

/// <summary>What a paste does with a pasted item whose name is taken in the folder.</summary>
public enum PasteMode
{
    /// <summary>Paste (Ctrl+V): asks, offering replace, rename or skip.</summary>
    Ask,

    /// <summary>Paste without replace (Ctrl+Alt+V): the pasted item gets a number, "report (2).pdf".</summary>
    KeepBoth,

    /// <summary>Paste with replace (Ctrl+Shift+V): the item that has the name is replaced.</summary>
    Replace,
}
