using System.IO;
using DesktopOrganizer.App.Models;
using Forms = System.Windows.Forms;

namespace DesktopOrganizer.App.Services;

public sealed class DesktopCapacityService
{
    private const int SafetyReserve = 2;
    private const int ConservativeDesktopLimit = 8;
    private readonly string _desktopDirectory;
    private readonly string _publicDesktopDirectory;
    private readonly Func<DesktopGridMetrics> _getGridMetrics;
    private readonly Func<string?, System.Drawing.Rectangle> _getWorkArea;
    private readonly Func<int?> _getShellDesktopItemCount;

    public DesktopCapacityService(
        string? desktopDirectory = null,
        string? publicDesktopDirectory = null,
        Func<DesktopGridMetrics>? getGridMetrics = null,
        Func<string?, System.Drawing.Rectangle>? getWorkArea = null,
        Func<int?>? getShellDesktopItemCount = null)
    {
        var hasDesktopOverride = desktopDirectory is not null
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DESKTOPORGANIZER_DESKTOP"));
        _desktopDirectory = desktopDirectory
            ?? GetPathOverride("DESKTOPORGANIZER_DESKTOP", Environment.SpecialFolder.DesktopDirectory);
        _publicDesktopDirectory = publicDesktopDirectory
            ?? GetPathOverride("DESKTOPORGANIZER_PUBLIC_DESKTOP", Environment.SpecialFolder.CommonDesktopDirectory);
        _getGridMetrics = getGridMetrics ?? new DesktopGridService().GetMetrics;
        _getWorkArea = getWorkArea ?? GetWorkArea;
        _getShellDesktopItemCount = getShellDesktopItemCount
            ?? (hasDesktopOverride ? () => null : TryGetShellDesktopItemCount);
    }

    public string DesktopDirectory => _desktopDirectory;

    public string PublicDesktopDirectory => _publicDesktopDirectory;

    public DesktopCapacityEstimate Estimate(string? monitorDeviceName = null)
    {
        try
        {
            var metrics = _getGridMetrics();
            var workArea = _getWorkArea(monitorDeviceName);
            var cellWidth = Math.Max(1, metrics.CellWidth * metrics.DpiScale);
            var cellHeight = Math.Max(1, metrics.CellHeight * metrics.DpiScale);
            var columns = Math.Max(1, (int)Math.Floor(workArea.Width / cellWidth));
            var rows = Math.Max(1, (int)Math.Floor(workArea.Height / cellHeight));
            var totalSlots = columns * rows;
            var fileSystemItems = CountDesktopItems(_desktopDirectory)
                + CountDesktopItems(_publicDesktopDirectory);
            var existingItems = Math.Max(fileSystemItems, _getShellDesktopItemCount() ?? fileSystemItems);
            var available = Math.Max(0, totalSlots - existingItems - SafetyReserve);
            available = Math.Min(ConservativeDesktopLimit, available);

            return new DesktopCapacityEstimate(
                totalSlots,
                existingItems,
                SafetyReserve,
                available,
                true,
                "桌面容量为估算值，已预留 2 格并将本次桌面释放限制为最多 8 个。");
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or System.Runtime.InteropServices.ExternalException)
        {
            return new DesktopCapacityEstimate(
                0,
                0,
                SafetyReserve,
                ConservativeDesktopLimit,
                true,
                "无法可靠读取桌面网格，按估算值最多释放 8 个到桌面。");
        }
    }

    private static int CountDesktopItems(string directory)
    {
        return Directory.Exists(directory)
            ? Directory.EnumerateFileSystemEntries(directory).Count()
            : 0;
    }

    private static string GetPathOverride(string environmentVariable, Environment.SpecialFolder fallback)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : Environment.GetFolderPath(fallback);
    }

    private static System.Drawing.Rectangle GetWorkArea(string? monitorDeviceName)
    {
        var screen = Forms.Screen.AllScreens.FirstOrDefault(candidate =>
            string.Equals(candidate.DeviceName, monitorDeviceName, StringComparison.OrdinalIgnoreCase));
        return (screen ?? Forms.Screen.PrimaryScreen)?.WorkingArea
            ?? new System.Drawing.Rectangle(0, 0, 1280, 720);
    }

    private static int? TryGetShellDesktopItemCount()
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return null;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic desktop = shell.NameSpace(0);
            return (int)desktop.Items().Count;
        }
        catch
        {
            return null;
        }
    }
}
