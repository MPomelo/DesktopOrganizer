namespace DesktopOrganizer.App.Models;

public sealed record DesktopCapacityEstimate(
    int TotalSlots,
    int ExistingItems,
    int SafetyReserve,
    int SafeAvailableSlots,
    bool IsEstimate,
    string Explanation);

public enum ContainerReleaseDestination
{
    None,
    Desktop,
    StartMenu
}

public enum ContainerReleaseOperation
{
    None,
    MoveFile,
    CreateShortcut,
    CreateUrl
}

public sealed class ContainerReleaseAction
{
    public string ItemId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public ContainerReleaseDestination Destination { get; set; }

    public ContainerReleaseOperation Operation { get; set; }

    public string SourcePath { get; set; } = "";

    public string TargetPath { get; set; } = "";

    public string LaunchTarget { get; set; } = "";

    public string Arguments { get; set; } = "";

    public string WorkingDirectory { get; set; } = "";

    public string IconSource { get; set; } = "";
}

public sealed class ContainerReleasePlan
{
    public string ContainerId { get; set; } = "";

    public int TotalCount { get; set; }

    public int DesktopCount { get; set; }

    public int StartMenuCount { get; set; }

    public int NoActionCount { get; set; }

    public int MaximumDesktopCount { get; set; }

    public bool IsCapacityEstimate { get; set; }

    public string CapacityExplanation { get; set; } = "";

    public string DesktopDirectory { get; set; } = "";

    public string StartMenuDirectory { get; set; } = "";

    public List<ContainerReleaseAction> Actions { get; set; } = [];
}

public sealed record ContainerReleaseResult(
    int DesktopCount,
    int StartMenuCount,
    int NoActionCount,
    string StartMenuDirectory);

public sealed class ContainerReleaseJournal
{
    public string ContainerId { get; set; } = "";

    public DateTimeOffset StartedAt { get; set; }

    public bool ConfigurationCommitted { get; set; }

    public List<ContainerReleaseJournalEntry> Entries { get; set; } = [];
}

public sealed class ContainerReleaseJournalEntry
{
    public ContainerReleaseOperation Operation { get; set; }

    public string SourcePath { get; set; } = "";

    public string TargetPath { get; set; } = "";

    public bool Completed { get; set; }
}
