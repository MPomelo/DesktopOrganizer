using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using DesktopOrganizer.App.Models;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using SystemColors = System.Windows.SystemColors;

namespace DesktopOrganizer.App.Services;

public sealed class ThemeService
{
    private readonly Func<Color> _accentColorProvider;
    private readonly Func<bool> _highContrastProvider;

    public ThemeService(
        Func<Color>? accentColorProvider = null,
        Func<bool>? highContrastProvider = null)
    {
        _accentColorProvider = accentColorProvider ?? GetSystemAccentColor;
        _highContrastProvider = highContrastProvider ?? (() => SystemParameters.HighContrast);
    }

    public ResolvedContainerTheme Resolve(ContainerStyleConfig style)
    {
        ArgumentNullException.ThrowIfNull(style);

        if (_highContrastProvider())
        {
            return new ResolvedContainerTheme(
                SystemColors.HighlightColor,
                SystemColors.WindowColor,
                SystemColors.ControlColor,
                SystemColors.HighlightColor);
        }

        var accent = WithAlpha(_accentColorProvider(), byte.MaxValue);
        var borderSetting = style.BorderColor ?? ThemeColorSetting.SystemAccent();
        var backgroundSetting = style.BackgroundColor ?? ThemeColorSetting.SystemAccent();
        var moreSetting = style.MoreButtonColor ?? ThemeColorSetting.Auto();
        var hoverSetting = style.HoverBackgroundColor ?? ThemeColorSetting.Auto();
        var backgroundBase = ResolveBaseColor(backgroundSetting, accent, accent);
        var borderBase = ResolveBaseColor(borderSetting, accent, accent);
        var contrast = IsLight(backgroundBase) ? Colors.Black : Colors.White;
        var moreBase = ResolveBaseColor(moreSetting, accent, contrast);
        var hoverBase = ResolveBaseColor(hoverSetting, accent, contrast);

        return new ResolvedContainerTheme(
            WithAlpha(borderBase, 230),
            WithAlpha(backgroundBase, OpacityToByte(style.BackgroundOpacity)),
            WithAlpha(moreBase, moreSetting.Mode == ThemeColorMode.Auto ? (byte)55 : (byte)180),
            WithAlpha(hoverBase, hoverSetting.Mode == ThemeColorMode.Auto ? (byte)46 : (byte)100));
    }

    public static bool TryNormalizeHexColor(string? value, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        if (text.StartsWith('#'))
        {
            text = text[1..];
        }

        if (text.Length is not (6 or 8)
            || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
        {
            return false;
        }

        normalized = $"#{text.ToUpperInvariant()}";
        return true;
    }

    internal static Color ParseHexColor(string normalized)
    {
        var text = normalized[1..];
        if (text.Length == 6)
        {
            return Color.FromRgb(
                byte.Parse(text[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(text.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(text.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }

        return Color.FromArgb(
            byte.Parse(text[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(text.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(text.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(text.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    private static Color ResolveBaseColor(
        ThemeColorSetting? setting,
        Color accent,
        Color automaticColor)
    {
        if (setting is null)
        {
            return accent;
        }

        return setting.Mode switch
        {
            ThemeColorMode.Custom
                when TryNormalizeHexColor(setting.HexColor, out var normalized) =>
                ParseHexColor(normalized),
            ThemeColorMode.Auto => automaticColor,
            _ => accent
        };
    }

    private static byte OpacityToByte(double opacity)
    {
        var safeOpacity = double.IsFinite(opacity) ? Math.Clamp(opacity, 0.15, 0.9) : 0.45;
        return (byte)Math.Round(byte.MaxValue * safeOpacity);
    }

    private static Color WithAlpha(Color color, byte alpha)
    {
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    private static bool IsLight(Color color)
    {
        var luminance = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255;
        return luminance >= 0.55;
    }

    private static Color GetSystemAccentColor()
    {
        try
        {
            if (DwmGetColorizationColor(out var colorization, out _) == 0)
            {
                return Color.FromRgb(
                    (byte)(colorization >> 16),
                    (byte)(colorization >> 8),
                    (byte)colorization);
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }

        var fallback = SystemParameters.WindowGlassColor;
        return Color.FromRgb(fallback.R, fallback.G, fallback.B);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetColorizationColor(
        out uint colorization,
        [MarshalAs(UnmanagedType.Bool)] out bool opaqueBlend);
}
