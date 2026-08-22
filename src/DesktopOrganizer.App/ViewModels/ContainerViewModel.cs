using System.IO;
using System.Windows.Input;
using DesktopOrganizer.App.Models;
using DesktopOrganizer.App.Services;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace DesktopOrganizer.App.ViewModels;

public sealed class ContainerViewModel : ViewModelBase
{
    private static readonly (string Glyph, string Label)[] PlaceholderDefinitions =
    [
        ("\uE7B8", "应用"),
        ("\uE8B7", "文件夹"),
        ("\uE8A5", "文档")
    ];

    private readonly DesktopGridMetrics _gridMetrics;
    private readonly ContainerConfig _config;
    private readonly BehaviorConfig _behavior;
    private readonly DragDropImportService _dragDropImportService;
    private readonly IIconLaunchService _iconLaunchService;
    private readonly ILogger _logger;
    private readonly Func<Task> _persistConfigurationAsync;
    private readonly ThemeService _themeService;
    private ContainerSizeMode _sizeMode;
    private ContainerOrientation _orientation;
    private int _rows;
    private int _columns;
    private double _windowWidth;
    private double _windowHeight;
    private double _iconGlyphSize;
    private IReadOnlyList<IconTileViewModel> _tiles = [];
    private IReadOnlyList<IconTileViewModel> _allItemTiles = [];
    private double _containerBorderThickness;
    private Brush _containerBorderBrush = Brushes.Transparent;
    private Brush _containerBackgroundBrush = Brushes.Transparent;
    private Brush _moreButtonBrush = Brushes.Transparent;
    private Brush _hoverBackgroundBrush = Brushes.Transparent;
    private ContainerSettingsState? _settingsOriginalState;

    public ContainerViewModel(
        DesktopGridService desktopGridService,
        ContainerConfig config,
        BehaviorConfig behavior,
        DragDropImportService dragDropImportService,
        IIconLaunchService iconLaunchService,
        ILogger logger,
        Func<Task> persistConfigurationAsync,
        ThemeService? themeService = null)
    {
        _config = config;
        _behavior = behavior;
        _dragDropImportService = dragDropImportService;
        _iconLaunchService = iconLaunchService;
        _logger = logger;
        _persistConfigurationAsync = persistConfigurationAsync;
        _themeService = themeService ?? new ThemeService();
        _gridMetrics = desktopGridService.GetMetrics();
        _sizeMode = config.SizeMode;
        _orientation = config.Orientation;

        SetTwoByTwoCommand = new RelayCommand(() => SetSizeMode(ContainerSizeMode.TwoByTwo));
        SetOneByFourCommand = new RelayCommand(() => SetSizeMode(ContainerSizeMode.OneByFour));
        SetTwoByFourCommand = new RelayCommand(() => SetSizeMode(ContainerSizeMode.TwoByFour));
        SetHorizontalCommand = new RelayCommand(
            () => SetOrientation(ContainerOrientation.Horizontal),
            () => SupportsOrientation);
        SetVerticalCommand = new RelayCommand(
            () => SetOrientation(ContainerOrientation.Vertical),
            () => SupportsOrientation);

        RefreshLayout();
        RefreshTheme();
    }

    public event EventHandler? ConfigurationChanged;

    public string Name => _config.Name;

    public ContainerSizeMode SizeMode => _sizeMode;

    public ContainerOrientation Orientation => _orientation;

    public int Rows
    {
        get => _rows;
        private set => SetProperty(ref _rows, value);
    }

    public int Columns
    {
        get => _columns;
        private set => SetProperty(ref _columns, value);
    }

    public double WindowWidth
    {
        get => _windowWidth;
        private set => SetProperty(ref _windowWidth, value);
    }

    public double WindowHeight
    {
        get => _windowHeight;
        private set => SetProperty(ref _windowHeight, value);
    }

    public double IconGlyphSize
    {
        get => _iconGlyphSize;
        private set => SetProperty(ref _iconGlyphSize, value);
    }

    public IReadOnlyList<IconTileViewModel> Tiles
    {
        get => _tiles;
        private set => SetProperty(ref _tiles, value);
    }

    public IReadOnlyList<IconTileViewModel> AllItemTiles
    {
        get => _allItemTiles;
        private set => SetProperty(ref _allItemTiles, value);
    }

    public bool IsTwoByTwo => SizeMode == ContainerSizeMode.TwoByTwo;

    public bool IsOneByFour => SizeMode == ContainerSizeMode.OneByFour;

    public bool IsTwoByFour => SizeMode == ContainerSizeMode.TwoByFour;

    public bool IsHorizontal => Orientation == ContainerOrientation.Horizontal;

    public bool IsVertical => Orientation == ContainerOrientation.Vertical;

    public bool SupportsOrientation => SizeMode != ContainerSizeMode.TwoByTwo;

    public IReadOnlyList<StoredIconItem> StoredItems => _config.Items;

    public bool HasStoredItems => _config.Items.Count > 0;

    public int StoredItemCount => _config.Items.Count;

    public bool HasActiveSettingsPreview => _settingsOriginalState is not null;

    public double ContainerBorderThickness
    {
        get => _containerBorderThickness;
        private set => SetProperty(ref _containerBorderThickness, value);
    }

    public Brush ContainerBorderBrush
    {
        get => _containerBorderBrush;
        private set => SetProperty(ref _containerBorderBrush, value);
    }

    public Brush ContainerBackgroundBrush
    {
        get => _containerBackgroundBrush;
        private set => SetProperty(ref _containerBackgroundBrush, value);
    }

    public Brush MoreButtonBrush
    {
        get => _moreButtonBrush;
        private set => SetProperty(ref _moreButtonBrush, value);
    }

    public Brush HoverBackgroundBrush
    {
        get => _hoverBackgroundBrush;
        private set => SetProperty(ref _hoverBackgroundBrush, value);
    }

    public ICommand SetTwoByTwoCommand { get; }

    public ICommand SetOneByFourCommand { get; }

    public ICommand SetTwoByFourCommand { get; }

    public RelayCommand SetHorizontalCommand { get; }

    public RelayCommand SetVerticalCommand { get; }

    public bool NeedsDesktopShortcutImportChoice(IEnumerable<string> paths)
    {
        return !_behavior.HasChosenDesktopShortcutImportBehavior
            && _dragDropImportService.ContainsDesktopShortcut(paths);
    }

    public void SetDesktopShortcutImportBehavior(bool moveToManagedStorage)
    {
        _behavior.HasChosenDesktopShortcutImportBehavior = true;
        _behavior.MoveDesktopShortcutsToManagedStorage = moveToManagedStorage;
    }

    public async Task<ImportDisplayResult> ImportFilesAsync(
        IEnumerable<string> paths,
        CancellationToken cancellationToken = default)
    {
        var batch = await _dragDropImportService.ImportAsync(
            paths,
            _config.Items,
            _behavior.MoveDesktopShortcutsToManagedStorage,
            cancellationToken);

        foreach (var artifact in batch.Imported)
        {
            _config.Items.Add(artifact.Item);
            if (!_config.PinnedIconIds.Contains(artifact.Item.Id, StringComparer.Ordinal))
            {
                _config.PinnedIconIds.Add(artifact.Item.Id);
            }
        }

        try
        {
            await _persistConfigurationAsync();
        }
        catch
        {
            foreach (var artifact in batch.Imported)
            {
                _config.Items.Remove(artifact.Item);
                _config.PinnedIconIds.Remove(artifact.Item.Id);
            }

            await _dragDropImportService.RollbackAsync(batch);
            RefreshLayout();
            throw;
        }

        OnPropertyChanged(nameof(StoredItems));
        OnPropertyChanged(nameof(HasStoredItems));
        OnPropertyChanged(nameof(StoredItemCount));
        RefreshLayout();
        return new ImportDisplayResult(batch.Imported.Count, batch.Failures);
    }

    public async Task LaunchItemAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        var item = _config.Items.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, itemId, StringComparison.Ordinal));
        if (item is null)
        {
            throw new InvalidOperationException("The selected icon item no longer exists.");
        }

        await _iconLaunchService.LaunchAsync(item, cancellationToken);
        item.LastLaunchedAt = DateTimeOffset.Now;

        try
        {
            await _persistConfigurationAsync();
        }
        catch (Exception exception)
        {
            _logger.Error(
                $"Opened '{item.DisplayName}', but failed to save its recent launch time.",
                exception);
        }
    }

    public ContainerSettingsState BeginSettingsPreview()
    {
        if (_settingsOriginalState is not null)
        {
            throw new InvalidOperationException("A settings preview is already active.");
        }

        _settingsOriginalState = ContainerSettingsState.Capture(_config);
        return _settingsOriginalState.Clone();
    }

    public void PreviewSettings(ContainerSettingsState state)
    {
        if (_settingsOriginalState is null)
        {
            throw new InvalidOperationException("A settings preview has not been started.");
        }

        ApplySettingsState(state);
    }

    public async Task CommitSettingsPreviewAsync()
    {
        if (_settingsOriginalState is null)
        {
            throw new InvalidOperationException("A settings preview has not been started.");
        }

        var restoredFiles = new List<(string RestoredPath, string ManagedPath)>();
        try
        {
            RestoreRemovedManagedItems(restoredFiles);
            await _persistConfigurationAsync();
            _settingsOriginalState = null;
        }
        catch
        {
            RollbackRestoredManagedItems(restoredFiles);
            CancelSettingsPreview();
            throw;
        }
    }

    public void CancelSettingsPreview()
    {
        if (_settingsOriginalState is null)
        {
            return;
        }

        var originalState = _settingsOriginalState;
        _settingsOriginalState = null;
        ApplySettingsState(originalState);
    }

    private void SetSizeMode(ContainerSizeMode value)
    {
        if (_sizeMode == value)
        {
            return;
        }

        _sizeMode = value;
        _config.SizeMode = value;
        RefreshLayoutState();
        ConfigurationChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetOrientation(ContainerOrientation value)
    {
        if (!SupportsOrientation || _orientation == value)
        {
            return;
        }

        _orientation = value;
        _config.Orientation = value;
        RefreshLayoutState();
        ConfigurationChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshLayoutState()
    {
        OnPropertyChanged(nameof(SizeMode));
        OnPropertyChanged(nameof(Orientation));
        OnPropertyChanged(nameof(IsTwoByTwo));
        OnPropertyChanged(nameof(IsOneByFour));
        OnPropertyChanged(nameof(IsTwoByFour));
        OnPropertyChanged(nameof(IsHorizontal));
        OnPropertyChanged(nameof(IsVertical));
        OnPropertyChanged(nameof(SupportsOrientation));

        SetHorizontalCommand.NotifyCanExecuteChanged();
        SetVerticalCommand.NotifyCanExecuteChanged();
        RefreshLayout();
    }

    private void ApplySettingsState(ContainerSettingsState state)
    {
        state.ApplyTo(_config);
        _sizeMode = _config.SizeMode;
        _orientation = _config.Orientation;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(StoredItems));
        OnPropertyChanged(nameof(HasStoredItems));
        OnPropertyChanged(nameof(StoredItemCount));
        RefreshLayoutState();
        RefreshTheme();
    }

    private void RestoreRemovedManagedItems(
        ICollection<(string RestoredPath, string ManagedPath)> restoredFiles)
    {
        if (_settingsOriginalState is null)
        {
            return;
        }

        var retainedIds = _config.Items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var removedItem in _settingsOriginalState.Items.Where(item =>
            !retainedIds.Contains(item.Id)
            && item.StorageMode == IconStorageMode.MovedToManagedStorage
            && !string.IsNullOrWhiteSpace(item.ManagedPath)
            && !string.IsNullOrWhiteSpace(item.OriginalPath)
            && File.Exists(item.ManagedPath)))
        {
            var restorePath = GetAvailableRestorePath(itemPath: removedItem.OriginalPath);
            var restoreDirectory = Path.GetDirectoryName(restorePath);
            if (!string.IsNullOrWhiteSpace(restoreDirectory))
            {
                Directory.CreateDirectory(restoreDirectory);
            }

            File.Move(removedItem.ManagedPath, restorePath);
            restoredFiles.Add((restorePath, removedItem.ManagedPath));
            _logger.Info(
                $"Restored removed managed shortcut '{removedItem.ManagedPath}' to '{restorePath}'.");
        }
    }

    private void RollbackRestoredManagedItems(
        IEnumerable<(string RestoredPath, string ManagedPath)> restoredFiles)
    {
        foreach (var move in restoredFiles.Reverse())
        {
            try
            {
                if (File.Exists(move.RestoredPath) && !File.Exists(move.ManagedPath))
                {
                    var managedDirectory = Path.GetDirectoryName(move.ManagedPath);
                    if (!string.IsNullOrWhiteSpace(managedDirectory))
                    {
                        Directory.CreateDirectory(managedDirectory);
                    }

                    File.Move(move.RestoredPath, move.ManagedPath);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.Error(
                    $"Failed to roll back restored shortcut '{move.RestoredPath}'.",
                    exception);
            }
        }
    }

    private static string GetAvailableRestorePath(string itemPath)
    {
        if (!File.Exists(itemPath) && !Directory.Exists(itemPath))
        {
            return itemPath;
        }

        var directory = Path.GetDirectoryName(itemPath) ?? "";
        var fileName = Path.GetFileNameWithoutExtension(itemPath);
        var extension = Path.GetExtension(itemPath);
        for (var index = 2; index < 10_000; index++)
        {
            var candidate = Path.Combine(directory, $"{fileName} ({index}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"Could not find an available restore path for '{itemPath}'.");
    }

    private void RefreshLayout()
    {
        var layout = ContainerLayoutCalculator.Calculate(SizeMode, Orientation, _gridMetrics);
        Rows = layout.Rows;
        Columns = layout.Columns;
        WindowWidth = layout.Width;
        WindowHeight = layout.Height;
        IconGlyphSize = Math.Clamp(_gridMetrics.IconSize, 28, 44);
        AllItemTiles = _config.Items.Select(IconTileViewModel.CreateStoredItem).ToArray();
        Tiles = CreateTiles(layout.Rows * layout.Columns);
    }

    private void RefreshTheme()
    {
        var theme = _themeService.Resolve(_config.Style);
        ContainerBorderThickness = Math.Clamp(_config.Style.BorderThickness, 1, 12);
        ContainerBorderBrush = CreateBrush(theme.BorderColor);
        ContainerBackgroundBrush = CreateBrush(theme.BackgroundColor);
        MoreButtonBrush = CreateBrush(theme.MoreButtonColor);
        HoverBackgroundBrush = CreateBrush(theme.HoverBackgroundColor);
    }

    private static Brush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private IReadOnlyList<IconTileViewModel> CreateTiles(int slotCount)
    {
        var tiles = new List<IconTileViewModel>(slotCount);
        var visibleItemCount = slotCount - 1;
        var itemsById = _config.Items.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var visibleItems = new List<StoredIconItem>(visibleItemCount);

        foreach (var pinnedId in _config.PinnedIconIds)
        {
            if (visibleItems.Count == visibleItemCount)
            {
                break;
            }

            if (itemsById.TryGetValue(pinnedId, out var item) && !visibleItems.Contains(item))
            {
                visibleItems.Add(item);
            }
        }

        tiles.AddRange(visibleItems.Select(IconTileViewModel.CreateStoredItem));

        for (var index = visibleItems.Count; index < visibleItemCount; index++)
        {
            var definition = PlaceholderDefinitions[index % PlaceholderDefinitions.Length];
            tiles.Add(IconTileViewModel.CreatePlaceholder(
                definition.Glyph,
                definition.Label,
                index + 1));
        }

        tiles.Add(IconTileViewModel.CreateMoreButton());
        return tiles;
    }
}
