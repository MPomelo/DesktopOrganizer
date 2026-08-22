using System.Runtime.InteropServices;
using DesktopOrganizer.App.Models;

namespace DesktopOrganizer.App.Services;

public sealed class DesktopGridService
{
    private const int SmCxIcon = 11;
    private const int SmCyIcon = 12;
    private const int SmCxIconSpacing = 38;
    private const int SmCyIconSpacing = 39;
    private const double DefaultCellSize = 96;
    private const double DefaultIconSize = 32;
    private const uint DefaultDpi = 96;

    public DesktopGridMetrics GetMetrics()
    {
        try
        {
            var dpi = GetDpiForSystem();
            if (dpi < DefaultDpi)
            {
                dpi = DefaultDpi;
            }

            var dpiScale = dpi / (double)DefaultDpi;
            var cellWidth = ToDeviceIndependentUnits(
                GetSystemMetricsForDpi(SmCxIconSpacing, dpi),
                dpiScale,
                DefaultCellSize);
            var cellHeight = ToDeviceIndependentUnits(
                GetSystemMetricsForDpi(SmCyIconSpacing, dpi),
                dpiScale,
                DefaultCellSize);
            var iconWidth = ToDeviceIndependentUnits(
                GetSystemMetricsForDpi(SmCxIcon, dpi),
                dpiScale,
                DefaultIconSize);
            var iconHeight = ToDeviceIndependentUnits(
                GetSystemMetricsForDpi(SmCyIcon, dpi),
                dpiScale,
                DefaultIconSize);

            return new DesktopGridMetrics(
                ValidateCellSize(cellWidth),
                ValidateCellSize(cellHeight),
                Math.Clamp(Math.Min(iconWidth, iconHeight), 24, 64),
                dpiScale);
        }
        catch (EntryPointNotFoundException)
        {
            return CreateFallbackMetrics();
        }
    }

    private static double ToDeviceIndependentUnits(int pixels, double dpiScale, double fallback)
    {
        return pixels > 0 ? pixels / dpiScale : fallback;
    }

    private static double ValidateCellSize(double value)
    {
        return value is >= 48 and <= 256 ? value : DefaultCellSize;
    }

    private static DesktopGridMetrics CreateFallbackMetrics()
    {
        return new DesktopGridMetrics(DefaultCellSize, DefaultCellSize, DefaultIconSize, 1);
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);
}
