using System.Windows.Input;
using System.Windows.Media;
using DesktopOrganizer.App.Models;

namespace DesktopOrganizer.App.ViewModels;

public sealed class SettingsIconItemViewModel : ViewModelBase
{
    private readonly Action<SettingsIconItemViewModel> _moveUp;
    private readonly Action<SettingsIconItemViewModel> _moveDown;
    private readonly Action<SettingsIconItemViewModel> _delete;
    private string _displayName;
    private bool _isPinned;

    public SettingsIconItemViewModel(
        StoredIconItem item,
        bool isPinned,
        Action<SettingsIconItemViewModel> moveUp,
        Action<SettingsIconItemViewModel> moveDown,
        Action<SettingsIconItemViewModel> delete)
    {
        Item = ContainerSettingsState.CloneItem(item);
        _displayName = Item.DisplayName;
        _isPinned = isPinned;
        _moveUp = moveUp;
        _moveDown = moveDown;
        _delete = delete;

        var tile = IconTileViewModel.CreateStoredItem(Item);
        Icon = tile.Icon;
        Glyph = tile.Glyph;
        MoveUpCommand = new RelayCommand(() => _moveUp(this), () => CanMoveUp);
        MoveDownCommand = new RelayCommand(() => _moveDown(this), () => CanMoveDown);
        DeleteCommand = new RelayCommand(() => _delete(this));
    }

    public event EventHandler? SettingChanged;

    internal StoredIconItem Item { get; }

    public string Id => Item.Id;

    public ImageSource? Icon { get; }

    public bool HasIcon => Icon is not null;

    public string Glyph { get; }

    public string KindLabel => Item.Kind switch
    {
        IconItemKind.Shortcut => "快捷方式",
        IconItemKind.File => "文件",
        IconItemKind.Folder => "文件夹",
        IconItemKind.Url => "网址",
        IconItemKind.App => "应用",
        _ => "项目"
    };

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (SetProperty(ref _displayName, value))
            {
                SettingChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (SetProperty(ref _isPinned, value))
            {
                SettingChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool CanMoveUp { get; private set; }

    public bool CanMoveDown { get; private set; }

    public RelayCommand MoveUpCommand { get; }

    public RelayCommand MoveDownCommand { get; }

    public ICommand DeleteCommand { get; }

    internal void UpdatePosition(int index, int count)
    {
        CanMoveUp = index > 0;
        CanMoveDown = index < count - 1;
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    internal StoredIconItem CreateItem()
    {
        var clone = ContainerSettingsState.CloneItem(Item);
        clone.DisplayName = DisplayName.Trim();
        return clone;
    }
}
