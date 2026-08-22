using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopOrganizer.App.Models;

namespace DesktopOrganizer.App.Services;

public sealed class ContainerReleaseService
{
    private const string JournalFileName = "release-journal.json";
    private readonly string _appDirectory;
    private readonly DesktopCapacityService _capacityService;
    private readonly ShortcutService _shortcutService;
    private readonly ILogger _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Func<ContainerReleaseAction, int, Exception?>? _failureInjector;

    public ContainerReleaseService(
        string appDirectory,
        DesktopCapacityService capacityService,
        ShortcutService shortcutService,
        ILogger logger,
        string? startMenuDirectory = null,
        Func<ContainerReleaseAction, int, Exception?>? failureInjector = null)
    {
        _appDirectory = appDirectory;
        _capacityService = capacityService;
        _shortcutService = shortcutService;
        _logger = logger;
        StartMenuDirectory = startMenuDirectory
            ?? GetStartMenuDirectory();
        JournalPath = Path.Combine(appDirectory, JournalFileName);
        _failureInjector = failureInjector;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public string StartMenuDirectory { get; }

    public string JournalPath { get; }

    public ContainerReleasePlan CreatePlan(ContainerConfig container, int? desktopCount = null)
    {
        ArgumentNullException.ThrowIfNull(container);
        var capacity = _capacityService.Estimate(container.MonitorDeviceName);
        var candidates = container.Items.Where(item => !IsExistingDesktopReference(item)).ToList();
        var maximumDesktopCount = Math.Min(capacity.SafeAvailableSlots, candidates.Count);
        var selectedDesktopCount = Math.Clamp(desktopCount ?? maximumDesktopCount, 0, maximumDesktopCount);
        var reservedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var actions = new List<ContainerReleaseAction>(container.Items.Count);
        var desktopAssigned = 0;

        foreach (var item in container.Items)
        {
            if (IsExistingDesktopReference(item))
            {
                actions.Add(CreateNoAction(item));
                continue;
            }

            var destination = desktopAssigned < selectedDesktopCount
                ? ContainerReleaseDestination.Desktop
                : ContainerReleaseDestination.StartMenu;
            if (destination == ContainerReleaseDestination.Desktop)
            {
                desktopAssigned++;
            }

            actions.Add(CreateFileAction(item, destination, reservedPaths));
        }

        return new ContainerReleasePlan
        {
            ContainerId = container.Id,
            TotalCount = container.Items.Count,
            DesktopCount = actions.Count(action => action.Destination == ContainerReleaseDestination.Desktop),
            StartMenuCount = actions.Count(action => action.Destination == ContainerReleaseDestination.StartMenu),
            NoActionCount = actions.Count(action => action.Destination == ContainerReleaseDestination.None),
            MaximumDesktopCount = maximumDesktopCount,
            IsCapacityEstimate = capacity.IsEstimate,
            CapacityExplanation = capacity.Explanation,
            DesktopDirectory = _capacityService.DesktopDirectory,
            StartMenuDirectory = StartMenuDirectory,
            Actions = actions
        };
    }

    public async Task<ContainerReleaseResult> ExecuteAsync(
        AppConfig config,
        ContainerReleasePlan plan,
        Func<Task> saveConfigurationAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(saveConfigurationAsync);
        var containerIndex = config.Containers.FindIndex(container => container.Id == plan.ContainerId);
        if (containerIndex < 0)
        {
            throw new InvalidOperationException("要关闭的收纳区已不存在。");
        }

        Preflight(plan);
        Directory.CreateDirectory(_appDirectory);
        var journal = new ContainerReleaseJournal
        {
            ContainerId = plan.ContainerId,
            StartedAt = DateTimeOffset.UtcNow,
            Entries = plan.Actions
                .Where(action => action.Operation != ContainerReleaseOperation.None)
                .Select(action => new ContainerReleaseJournalEntry
                {
                    Operation = action.Operation,
                    SourcePath = action.SourcePath,
                    TargetPath = action.TargetPath
                })
                .ToList()
        };
        await WriteJournalAsync(journal, cancellationToken).ConfigureAwait(false);

        try
        {
            var operationIndex = 0;
            foreach (var action in plan.Actions.Where(action => action.Operation != ContainerReleaseOperation.None))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var injectedException = _failureInjector?.Invoke(action, operationIndex);
                if (injectedException is not null)
                {
                    throw injectedException;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(action.TargetPath)!);
                ExecuteAction(action);
                journal.Entries[operationIndex].Completed = true;
                operationIndex++;
                await WriteJournalAsync(journal, cancellationToken).ConfigureAwait(false);
            }

            var removedContainer = config.Containers[containerIndex];
            config.Containers.RemoveAt(containerIndex);
            config.Behavior.HasClosedAllContainers = config.Containers.Count == 0;
            try
            {
                await saveConfigurationAsync().ConfigureAwait(false);
            }
            catch
            {
                config.Containers.Insert(containerIndex, removedContainer);
                config.Behavior.HasClosedAllContainers = false;
                throw;
            }

            journal.ConfigurationCommitted = true;
            try
            {
                await WriteJournalAsync(journal, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.Error(
                    "Container configuration was committed, but the release journal could not be marked committed.",
                    exception);
            }
            TryDeleteJournal();
            TryDeleteEmptyManagedDirectories(plan);
            _logger.Info($"Released container '{plan.ContainerId}'.");
            return new ContainerReleaseResult(
                plan.DesktopCount,
                plan.StartMenuCount,
                plan.NoActionCount,
                plan.StartMenuDirectory);
        }
        catch (Exception exception)
        {
            _logger.Error($"Failed to release container '{plan.ContainerId}'. Rolling back.", exception);
            try
            {
                await RollBackAsync(journal, CancellationToken.None).ConfigureAwait(false);
                TryDeleteJournal();
            }
            catch (Exception rollbackException)
            {
                _logger.Error($"Release rollback remains incomplete. Recovery journal: '{JournalPath}'.", rollbackException);
                throw new AggregateException(
                    $"释放失败，且回滚未完成。恢复日志位于：{JournalPath}",
                    exception,
                    rollbackException);
            }

            throw;
        }
    }

    public async Task RecoverIfNeededAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(JournalPath))
        {
            return;
        }

        var journal = await ReadJournalAsync(cancellationToken).ConfigureAwait(false);
        var configurationStillContainsContainer = config.Containers.Any(container => container.Id == journal.ContainerId);
        if (journal.ConfigurationCommitted || !configurationStillContainsContainer)
        {
            TryDeleteJournal();
            _logger.Info("Removed a committed container release journal during startup recovery.");
            return;
        }

        await RollBackAsync(journal, cancellationToken).ConfigureAwait(false);
        TryDeleteJournal();
        _logger.Info("Rolled back an interrupted container release during startup.");
    }

    private ContainerReleaseAction CreateFileAction(
        StoredIconItem item,
        ContainerReleaseDestination destination,
        ISet<string> reservedPaths)
    {
        var directory = destination == ContainerReleaseDestination.Desktop
            ? _capacityService.DesktopDirectory
            : StartMenuDirectory;
        var isManaged = item.StorageMode == IconStorageMode.MovedToManagedStorage
            && !string.IsNullOrWhiteSpace(item.ManagedPath);
        var isUrl = item.Kind == IconItemKind.Url
            || string.Equals(Path.GetExtension(item.SourcePath), ".url", StringComparison.OrdinalIgnoreCase);
        var operation = isManaged
            ? ContainerReleaseOperation.MoveFile
            : isUrl ? ContainerReleaseOperation.CreateUrl : ContainerReleaseOperation.CreateShortcut;
        var extension = isUrl ? ".url" : ".lnk";
        var preferredName = isManaged
            ? Path.GetFileNameWithoutExtension(item.OriginalPath)
            : item.DisplayName;
        var targetPath = ShortcutService.GetAvailablePath(directory, preferredName, extension, reservedPaths);

        return new ContainerReleaseAction
        {
            ItemId = item.Id,
            DisplayName = item.DisplayName,
            Destination = destination,
            Operation = operation,
            SourcePath = isManaged ? item.ManagedPath : item.SourcePath,
            TargetPath = targetPath,
            LaunchTarget = !string.IsNullOrWhiteSpace(item.LaunchTarget) ? item.LaunchTarget : item.SourcePath,
            Arguments = item.Arguments,
            WorkingDirectory = item.WorkingDirectory,
            IconSource = item.IconSource
        };
    }

    private static ContainerReleaseAction CreateNoAction(StoredIconItem item)
    {
        return new ContainerReleaseAction
        {
            ItemId = item.Id,
            DisplayName = item.DisplayName,
            Destination = ContainerReleaseDestination.None,
            Operation = ContainerReleaseOperation.None,
            SourcePath = item.SourcePath
        };
    }

    private bool IsExistingDesktopReference(StoredIconItem item)
    {
        if (item.StorageMode == IconStorageMode.MovedToManagedStorage)
        {
            return false;
        }

        return IsExistingPathUnder(item.SourcePath, _capacityService.DesktopDirectory)
            || IsExistingPathUnder(item.SourcePath, _capacityService.PublicDesktopDirectory)
            || IsExistingPathUnder(item.OriginalPath, _capacityService.DesktopDirectory)
            || IsExistingPathUnder(item.OriginalPath, _capacityService.PublicDesktopDirectory);
    }

    private static bool IsExistingPathUnder(string path, string directory)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory)
            || (!File.Exists(path) && !Directory.Exists(path)))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path);
        var fullDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory))
            + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static void Preflight(ContainerReleasePlan plan)
    {
        foreach (var action in plan.Actions)
        {
            if (action.Operation == ContainerReleaseOperation.MoveFile && !File.Exists(action.SourcePath))
            {
                throw new FileNotFoundException($"托管快捷方式不存在：{action.SourcePath}", action.SourcePath);
            }

            if (action.Operation == ContainerReleaseOperation.CreateShortcut
                && string.IsNullOrWhiteSpace(action.LaunchTarget))
            {
                throw new InvalidDataException($"“{action.DisplayName}”没有可用的启动目标。");
            }

            if (action.Operation != ContainerReleaseOperation.None
                && (File.Exists(action.TargetPath) || Directory.Exists(action.TargetPath)))
            {
                throw new IOException($"释放目标已被占用：{action.TargetPath}");
            }
        }
    }

    private void ExecuteAction(ContainerReleaseAction action)
    {
        switch (action.Operation)
        {
            case ContainerReleaseOperation.MoveFile:
                File.Move(action.SourcePath, action.TargetPath);
                break;
            case ContainerReleaseOperation.CreateShortcut:
                _shortcutService.CreateShortcut(action.TargetPath, action);
                break;
            case ContainerReleaseOperation.CreateUrl:
                _shortcutService.CreateUrl(action.TargetPath, action);
                break;
        }
    }

    private async Task RollBackAsync(ContainerReleaseJournal journal, CancellationToken cancellationToken)
    {
        foreach (var entry in journal.Entries.Where(entry => entry.Completed).Reverse())
        {
            if (entry.Operation == ContainerReleaseOperation.MoveFile)
            {
                if (File.Exists(entry.TargetPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(entry.SourcePath)!);
                    File.Move(entry.TargetPath, entry.SourcePath);
                }
            }
            else if (File.Exists(entry.TargetPath))
            {
                File.Delete(entry.TargetPath);
            }

            entry.Completed = false;
            await WriteJournalAsync(journal, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WriteJournalAsync(ContainerReleaseJournal journal, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_appDirectory);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(journal, _jsonOptions);
        await File.WriteAllBytesAsync(JournalPath, bytes, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ContainerReleaseJournal> ReadJournalAsync(CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(JournalPath);
        return await JsonSerializer.DeserializeAsync<ContainerReleaseJournal>(stream, _jsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException($"恢复日志无效：{JournalPath}");
    }

    private void TryDeleteJournal()
    {
        try
        {
            File.Delete(JournalPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Error($"Could not remove release journal '{JournalPath}'.", exception);
        }
    }

    private void TryDeleteEmptyManagedDirectories(ContainerReleasePlan plan)
    {
        var managedDirectories = plan.Actions
            .Where(action => action.Operation == ContainerReleaseOperation.MoveFile)
            .Select(action => Path.GetDirectoryName(action.SourcePath))
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in managedDirectories)
        {
            try
            {
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.Error($"Could not remove empty managed directory '{directory}'.", exception);
            }
        }
    }

    private static string GetStartMenuDirectory()
    {
        var overridePath = Environment.GetEnvironmentVariable("DESKTOPORGANIZER_START_MENU");
        return !string.IsNullOrWhiteSpace(overridePath)
            ? overridePath
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                "DesktopOrganizer");
    }
}
