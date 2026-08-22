using System.IO;
using DesktopOrganizer.App.Models;

namespace DesktopOrganizer.App.Services;

public sealed class DragDropImportService
{
    private readonly string _managedShortcutsDirectory;
    private readonly IconExtractionService _iconExtractionService;
    private readonly ShellLinkParser _shellLinkParser;
    private readonly ILogger _logger;
    private readonly string[] _desktopDirectories;

    public DragDropImportService(
        string appDirectory,
        IconExtractionService iconExtractionService,
        ShellLinkParser shellLinkParser,
        ILogger logger,
        IEnumerable<string>? desktopDirectories = null)
    {
        _managedShortcutsDirectory = Path.Combine(appDirectory, "ManagedShortcuts");
        _iconExtractionService = iconExtractionService;
        _shellLinkParser = shellLinkParser;
        _logger = logger;
        _desktopDirectories = (desktopDirectories ?? GetWindowsDesktopDirectories())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool ContainsDesktopShortcut(IEnumerable<string> paths)
    {
        return paths.Any(path => IsShortcut(path) && IsOnDesktop(path));
    }

    public async Task<ImportBatchResult> ImportAsync(
        IEnumerable<string> paths,
        IReadOnlyCollection<StoredIconItem> existingItems,
        bool moveDesktopShortcuts,
        CancellationToken cancellationToken = default)
    {
        var result = new ImportBatchResult();
        var knownPaths = existingItems
            .SelectMany(item => new[] { item.SourcePath, item.OriginalPath, item.ManagedPath })
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var rawPath in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var path = Path.GetFullPath(rawPath);
                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    throw new FileNotFoundException("The dropped item no longer exists.", path);
                }

                var normalizedPath = NormalizePath(path);
                if (knownPaths.Contains(normalizedPath))
                {
                    result.Failures.Add(new ImportFailure(path, "该项目已经在收纳区中。"));
                    continue;
                }

                var artifact = await ImportSingleAsync(
                    path,
                    moveDesktopShortcuts,
                    cancellationToken).ConfigureAwait(false);
                result.Imported.Add(artifact);
                knownPaths.Add(NormalizePath(artifact.Item.SourcePath));
                if (!string.IsNullOrWhiteSpace(artifact.Item.OriginalPath))
                {
                    knownPaths.Add(NormalizePath(artifact.Item.OriginalPath));
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.Error($"Failed to import '{rawPath}'.", exception);
                result.Failures.Add(new ImportFailure(rawPath, exception.Message));
            }
        }

        return result;
    }

    public Task RollbackAsync(ImportBatchResult result)
    {
        foreach (var artifact in result.Imported.AsEnumerable().Reverse())
        {
            if (!string.IsNullOrWhiteSpace(artifact.MovedFromPath)
                && File.Exists(artifact.Item.ManagedPath)
                && !File.Exists(artifact.MovedFromPath))
            {
                try
                {
                    File.Move(artifact.Item.ManagedPath, artifact.MovedFromPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    _logger.Error(
                        $"Failed to restore '{artifact.Item.ManagedPath}' to " +
                        $"'{artifact.MovedFromPath}'.",
                        exception);
                }
            }

            if (!string.IsNullOrWhiteSpace(artifact.CachePath))
            {
                TryDelete(artifact.CachePath);
            }
        }

        return Task.CompletedTask;
    }

    private async Task<ImportedIconArtifact> ImportSingleAsync(
        string path,
        bool moveDesktopShortcuts,
        CancellationToken cancellationToken)
    {
        var itemId = Guid.NewGuid().ToString("N");
        var isShortcut = IsShortcut(path);
        var linkInfo = isShortcut ? _shellLinkParser.Parse(path) : null;

        var iconCachePath = await _iconExtractionService.ExtractAndCacheAsync(
            path,
            itemId,
            cancellationToken).ConfigureAwait(false);

        string? movedFromPath = null;
        var sourcePath = path;
        var managedPath = "";
        var storageMode = IconStorageMode.ReferenceOnly;

        if (isShortcut && moveDesktopShortcuts && IsOnDesktop(path))
        {
            Directory.CreateDirectory(_managedShortcutsDirectory);
            managedPath = Path.Combine(_managedShortcutsDirectory, $"{itemId}.lnk");

            try
            {
                File.Move(path, managedPath);
                movedFromPath = path;
                sourcePath = managedPath;
                storageMode = IconStorageMode.MovedToManagedStorage;
            }
            catch
            {
                if (!string.IsNullOrWhiteSpace(iconCachePath))
                {
                    TryDelete(iconCachePath);
                }

                throw;
            }
        }

        var item = new StoredIconItem
        {
            Id = itemId,
            DisplayName = GetDisplayName(path),
            Kind = GetItemKind(path, isShortcut),
            SourcePath = sourcePath,
            LaunchTarget = isShortcut && !string.IsNullOrWhiteSpace(linkInfo?.TargetPath)
                ? linkInfo.TargetPath
                : sourcePath,
            Arguments = linkInfo?.Arguments ?? "",
            WorkingDirectory = linkInfo?.WorkingDirectory ?? "",
            IconSource = iconCachePath ?? "",
            StorageMode = storageMode,
            OriginalPath = path,
            ManagedPath = managedPath,
            AddedAt = DateTimeOffset.Now
        };

        return new ImportedIconArtifact(item, movedFromPath, iconCachePath);
    }

    private bool IsOnDesktop(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        return directory is not null
            && _desktopDirectories.Contains(NormalizePath(directory), StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetWindowsDesktopDirectories()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
    }

    private static IconItemKind GetItemKind(string path, bool isShortcut)
    {
        if (isShortcut)
        {
            return IconItemKind.Shortcut;
        }

        return Directory.Exists(path) ? IconItemKind.Folder : IconItemKind.File;
    }

    private static string GetDisplayName(string path)
    {
        var trimmedPath = Path.TrimEndingDirectorySeparator(path);
        return Directory.Exists(path)
            ? Path.GetFileName(trimmedPath)
            : Path.GetFileNameWithoutExtension(trimmedPath);
    }

    private static bool IsShortcut(string path)
    {
        return string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
