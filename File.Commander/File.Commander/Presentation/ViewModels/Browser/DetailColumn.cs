namespace File.Commander.Presentation.ViewModels.Browser;

/// <summary>
/// The detail columns of the list and tree (Name is not one: it is always shown and always first).
/// The declaration order is the default left-to-right order.
/// </summary>
public enum DetailColumn
{
    Size,
    Type,
    Modified,
    Created,
    Accessed,
    Permissions,
}
