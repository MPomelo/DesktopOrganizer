namespace DesktopOrganizer.App.Models;

public sealed class StoredIconItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string DisplayName { get; set; } = "";

    public IconItemKind Kind { get; set; }

    public string SourcePath { get; set; } = "";

    public string LaunchTarget { get; set; } = "";

    public string Arguments { get; set; } = "";

    public string WorkingDirectory { get; set; } = "";

    public string IconSource { get; set; } = "";

    public IconStorageMode StorageMode { get; set; }

    public string OriginalPath { get; set; } = "";

    public string ManagedPath { get; set; } = "";

    public DateTimeOffset AddedAt { get; set; }

    public DateTimeOffset? LastLaunchedAt { get; set; }
}

public enum IconItemKind
{
    Shortcut,
    File,
    Folder,
    Url,
    ShellItem,
    App
}

public enum IconStorageMode
{
    ReferenceOnly,
    MovedToManagedStorage,
    GeneratedShortcut
}
