namespace DesktopOrganizer.App.Models;

public sealed class WindowPositionChangedEventArgs(
    double left,
    double top,
    string monitorDeviceName) : EventArgs
{
    public double Left { get; } = left;

    public double Top { get; } = top;

    public string MonitorDeviceName { get; } = monitorDeviceName;
}
