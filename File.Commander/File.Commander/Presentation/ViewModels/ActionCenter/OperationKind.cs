namespace File.Commander.Presentation.ViewModels.ActionCenter;

/// <summary>What an action does. Picks its icon in the Action center.</summary>
public enum OperationKind
{
    Copy,
    Move,
    Delete,
    /// <summary>Deleted without the trash (Shift+Delete).</summary>
    DeletePermanently,
    EmptyTrash,
    /// <summary>A new version of File Commander being downloaded.</summary>
    Download,
    /// <summary>A downloaded version of File Commander replacing the running one.</summary>
    Install,
    Other,
}

public enum OperationState
{
    Running,
    Succeeded,
    /// <summary>It threw, or some items couldn't be processed.</summary>
    Failed,
    Canceled,
}
