namespace DesktopOrganizer.App.Models;

public sealed class ImportBatchResult
{
    public List<ImportedIconArtifact> Imported { get; } = [];

    public List<ImportFailure> Failures { get; } = [];
}

public sealed record ImportedIconArtifact(
    StoredIconItem Item,
    string? MovedFromPath,
    string? CachePath);

public sealed record ImportFailure(string Path, string Reason);

public sealed record ImportDisplayResult(
    int ImportedCount,
    IReadOnlyList<ImportFailure> Failures);
