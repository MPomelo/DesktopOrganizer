namespace DesktopOrganizer.App.Models;

public sealed class ContainerStyleConfig
{
    public double BorderThickness { get; set; } = 4;

    public double BackgroundOpacity { get; set; } = 0.45;

    public ThemeColorSetting BorderColor { get; set; } = ThemeColorSetting.SystemAccent();

    public ThemeColorSetting BackgroundColor { get; set; } = ThemeColorSetting.SystemAccent();

    public ThemeColorSetting MoreButtonColor { get; set; } = ThemeColorSetting.Auto();

    public ThemeColorSetting HoverBackgroundColor { get; set; } = ThemeColorSetting.Auto();
}

public sealed class ThemeColorSetting
{
    public ThemeColorMode Mode { get; set; } = ThemeColorMode.SystemAccent;

    public string? HexColor { get; set; }

    public static ThemeColorSetting SystemAccent() => new()
    {
        Mode = ThemeColorMode.SystemAccent
    };

    public static ThemeColorSetting Auto() => new()
    {
        Mode = ThemeColorMode.Auto
    };

    public static ThemeColorSetting Custom(string hexColor) => new()
    {
        Mode = ThemeColorMode.Custom,
        HexColor = hexColor
    };
}

public enum ThemeColorMode
{
    SystemAccent,
    Auto,
    Custom
}
