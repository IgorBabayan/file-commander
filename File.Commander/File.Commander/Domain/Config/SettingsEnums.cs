using System.Text.Json.Serialization;

namespace File.Commander.Domain.Config;

// Stored by name, so reordering the members doesn't change what a saved file means

[JsonConverter(typeof(JsonStringEnumConverter<OpenFileMode>))]
public enum OpenFileMode
{
    Click,
    DoubleClick
}

[JsonConverter(typeof(JsonStringEnumConverter<StartLocation>))]
public enum StartLocation
{
    Computer,
    Home,
    Recent
}

[JsonConverter(typeof(JsonStringEnumConverter<NewTabLocation>))]
public enum NewTabLocation
{
    CurrentDirectory,
    Computer,
    Home
}

/// <summary>Domain copy of the browser's DirectoryViewMode, so Domain doesn't depend on Presentation.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FolderViewMode>))]
public enum FolderViewMode
{
    Grid,
    List,
    Tree
}

/// <summary>Domain copy of the browser's FileSortMode (the sort menu), so Domain doesn't depend on Presentation.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FolderSortOrder>))]
public enum FolderSortOrder
{
    NameAscending,
    NameDescending,
    NewestFirst,
    OldestFirst,
    LargestFirst,
    Type
}
