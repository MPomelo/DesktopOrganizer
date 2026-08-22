using System.Collections.ObjectModel;
using DesktopOrganizer.App.Models;
using DesktopOrganizer.App.Services;
using Brush = System.Windows.Media.Brush;

namespace DesktopOrganizer.App.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly ContainerViewModel _containerViewModel;
    private string _name;
    private ContainerSizeMode _sizeMode;
    private ContainerOrientation _orientation;
    private double _borderThickness;
    private double _backgroundOpacity;
    private string _validationMessage = "";
    private bool _isCompleted;
    private bool _isInitializing = true;

    public SettingsViewModel(ContainerViewModel containerViewModel)
    {
        _containerViewModel = containerViewModel;
        var state = containerViewModel.BeginSettingsPreview();
        _name = state.Name;
        _sizeMode = state.SizeMode;
        _orientation = state.Orientation;
        _borderThickness = state.Style.BorderThickness;
        _backgroundOpacity = state.Style.BackgroundOpacity;

        BorderColor = new ColorSettingViewModel(state.Style.BorderColor, PreviewChanges);
        BackgroundColor = new ColorSettingViewModel(state.Style.BackgroundColor, PreviewChanges);
        MoreButtonColor = new ColorSettingViewModel(state.Style.MoreButtonColor, PreviewChanges);
        HoverBackgroundColor = new ColorSettingViewModel(
            state.Style.HoverBackgroundColor,
            PreviewChanges);

        var pinnedIds = state.PinnedIconIds.ToHashSet(StringComparer.Ordinal);
        IconItems = new ObservableCollection<SettingsIconItemViewModel>(
            state.Items.Select(item => CreateIconItem(item, pinnedIds.Contains(item.Id))));
        UpdateItemPositions();
        _isInitializing = false;
        PreviewChanges();
    }

    public IReadOnlyList<SelectionOption<ContainerSizeMode>> SizeOptions { get; } =
    [
        new(ContainerSizeMode.TwoByTwo, "2 x 2"),
        new(ContainerSizeMode.OneByFour, "1 x 4"),
        new(ContainerSizeMode.TwoByFour, "2 x 4")
    ];

    public IReadOnlyList<SelectionOption<ContainerOrientation>> OrientationOptions { get; } =
    [
        new(ContainerOrientation.Horizontal, "横向"),
        new(ContainerOrientation.Vertical, "纵向")
    ];

    public IReadOnlyList<SelectionOption<ThemeColorMode>> ColorModeOptions { get; } =
    [
        new(ThemeColorMode.SystemAccent, "系统主题"),
        new(ThemeColorMode.Auto, "自动"),
        new(ThemeColorMode.Custom, "自定义")
    ];

    public ObservableCollection<SettingsIconItemViewModel> IconItems { get; }

    public ColorSettingViewModel BorderColor { get; }

    public ColorSettingViewModel BackgroundColor { get; }

    public ColorSettingViewModel MoreButtonColor { get; }

    public ColorSettingViewModel HoverBackgroundColor { get; }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                PreviewChanges();
            }
        }
    }

    public ContainerSizeMode SizeMode
    {
        get => _sizeMode;
        set
        {
            if (SetProperty(ref _sizeMode, value))
            {
                OnPropertyChanged(nameof(SupportsOrientation));
                OnPropertyChanged(nameof(VisibleCapacity));
                OnPropertyChanged(nameof(PinnedSummary));
                PreviewChanges();
            }
        }
    }

    public ContainerOrientation Orientation
    {
        get => _orientation;
        set
        {
            if (SetProperty(ref _orientation, value))
            {
                PreviewChanges();
            }
        }
    }

    public bool SupportsOrientation => SizeMode != ContainerSizeMode.TwoByTwo;

    public int VisibleCapacity => SizeMode == ContainerSizeMode.TwoByFour ? 7 : 3;

    public string PinnedSummary => $"已选择 {IconItems.Count(item => item.IsPinned)}，当前显示前 {VisibleCapacity} 个";

    public double BorderThickness
    {
        get => _borderThickness;
        set
        {
            var safeValue = Math.Clamp(value, 1, 12);
            if (SetProperty(ref _borderThickness, safeValue))
            {
                PreviewChanges();
            }
        }
    }

    public double BackgroundOpacity
    {
        get => _backgroundOpacity;
        set
        {
            var safeValue = Math.Clamp(value, 0.15, 0.9);
            if (SetProperty(ref _backgroundOpacity, safeValue))
            {
                PreviewChanges();
            }
        }
    }

    public string ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value);
    }

    public Brush BorderPreviewBrush => _containerViewModel.ContainerBorderBrush;

    public Brush BackgroundPreviewBrush => _containerViewModel.ContainerBackgroundBrush;

    public Brush MoreButtonPreviewBrush => _containerViewModel.MoreButtonBrush;

    public Brush HoverPreviewBrush => _containerViewModel.HoverBackgroundBrush;

    public void MoveItem(SettingsIconItemViewModel item, int offset)
    {
        var oldIndex = IconItems.IndexOf(item);
        var newIndex = oldIndex + offset;
        if (oldIndex < 0 || newIndex < 0 || newIndex >= IconItems.Count)
        {
            return;
        }

        IconItems.Move(oldIndex, newIndex);
        UpdateItemPositions();
        PreviewChanges();
    }

    public void MoveItem(SettingsIconItemViewModel item, SettingsIconItemViewModel target)
    {
        var oldIndex = IconItems.IndexOf(item);
        var newIndex = IconItems.IndexOf(target);
        if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex)
        {
            return;
        }

        IconItems.Move(oldIndex, newIndex);
        UpdateItemPositions();
        PreviewChanges();
    }

    public void RemoveItem(SettingsIconItemViewModel item)
    {
        if (!IconItems.Remove(item))
        {
            return;
        }

        item.SettingChanged -= OnItemSettingChanged;
        UpdateItemPositions();
        OnPropertyChanged(nameof(PinnedSummary));
        PreviewChanges();
    }

    public void RestoreSystemTheme()
    {
        BorderColor.Mode = ThemeColorMode.SystemAccent;
        BackgroundColor.Mode = ThemeColorMode.SystemAccent;
        MoreButtonColor.Mode = ThemeColorMode.Auto;
        HoverBackgroundColor.Mode = ThemeColorMode.Auto;
        BorderThickness = 4;
        BackgroundOpacity = 0.45;
        PreviewChanges();
    }

    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ValidationMessage = "收纳区名称不能为空。";
            return false;
        }

        if (IconItems.Any(item => string.IsNullOrWhiteSpace(item.DisplayName)))
        {
            ValidationMessage = "图标名称不能为空。";
            return false;
        }

        foreach (var (label, setting) in GetColorSettings())
        {
            if (setting.Mode == ThemeColorMode.Custom
                && !ThemeService.TryNormalizeHexColor(setting.HexColor, out _))
            {
                ValidationMessage = $"{label}需要使用 #RRGGBB 或 #AARRGGBB 格式。";
                return false;
            }
        }

        ValidationMessage = "";
        return true;
    }

    public async Task SaveAsync()
    {
        if (!Validate())
        {
            throw new InvalidOperationException(ValidationMessage);
        }

        PreviewChanges();
        try
        {
            await _containerViewModel.CommitSettingsPreviewAsync();
            _isCompleted = true;
        }
        catch
        {
            _isCompleted = true;
            throw;
        }
    }

    public void Cancel()
    {
        if (_isCompleted)
        {
            return;
        }

        _containerViewModel.CancelSettingsPreview();
        _isCompleted = true;
    }

    private SettingsIconItemViewModel CreateIconItem(StoredIconItem item, bool isPinned)
    {
        var viewModel = new SettingsIconItemViewModel(
            item,
            isPinned,
            candidate => MoveItem(candidate, -1),
            candidate => MoveItem(candidate, 1),
            RemoveItem);
        viewModel.SettingChanged += OnItemSettingChanged;
        return viewModel;
    }

    private void OnItemSettingChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(PinnedSummary));
        PreviewChanges();
    }

    private void UpdateItemPositions()
    {
        for (var index = 0; index < IconItems.Count; index++)
        {
            IconItems[index].UpdatePosition(index, IconItems.Count);
        }
    }

    private void PreviewChanges()
    {
        if (_isInitializing || _isCompleted)
        {
            return;
        }

        _containerViewModel.PreviewSettings(CreateState());
        OnPropertyChanged(nameof(BorderPreviewBrush));
        OnPropertyChanged(nameof(BackgroundPreviewBrush));
        OnPropertyChanged(nameof(MoreButtonPreviewBrush));
        OnPropertyChanged(nameof(HoverPreviewBrush));
    }

    private ContainerSettingsState CreateState()
    {
        return new ContainerSettingsState
        {
            Name = Name,
            SizeMode = SizeMode,
            Orientation = Orientation,
            Items = IconItems.Select(item => item.CreateItem()).ToList(),
            PinnedIconIds = IconItems.Where(item => item.IsPinned).Select(item => item.Id).ToList(),
            Style = new ContainerStyleConfig
            {
                BorderThickness = BorderThickness,
                BackgroundOpacity = BackgroundOpacity,
                BorderColor = BorderColor.ToSetting(),
                BackgroundColor = BackgroundColor.ToSetting(),
                MoreButtonColor = MoreButtonColor.ToSetting(),
                HoverBackgroundColor = HoverBackgroundColor.ToSetting()
            }
        };
    }

    private IEnumerable<(string Label, ColorSettingViewModel Setting)> GetColorSettings()
    {
        yield return ("边框颜色", BorderColor);
        yield return ("背景颜色", BackgroundColor);
        yield return ("三点颜色", MoreButtonColor);
        yield return ("悬停颜色", HoverBackgroundColor);
    }
}

public sealed record SelectionOption<T>(T Value, string DisplayName);
