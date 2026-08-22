using DesktopOrganizer.App.Models;

namespace DesktopOrganizer.App.ViewModels;

public sealed class ColorSettingViewModel : ViewModelBase
{
    private readonly Action _changed;
    private ThemeColorMode _mode;
    private string _hexColor;

    public ColorSettingViewModel(ThemeColorSetting setting, Action changed)
    {
        _mode = setting.Mode;
        _hexColor = setting.HexColor ?? "#608CCB";
        _changed = changed;
    }

    public ThemeColorMode Mode
    {
        get => _mode;
        set
        {
            if (SetProperty(ref _mode, value))
            {
                OnPropertyChanged(nameof(IsCustom));
                _changed();
            }
        }
    }

    public string HexColor
    {
        get => _hexColor;
        set
        {
            if (SetProperty(ref _hexColor, value))
            {
                _changed();
            }
        }
    }

    public bool IsCustom => Mode == ThemeColorMode.Custom;

    public ThemeColorSetting ToSetting()
    {
        return new ThemeColorSetting
        {
            Mode = Mode,
            HexColor = HexColor
        };
    }
}
