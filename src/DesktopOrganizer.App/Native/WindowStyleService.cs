using System.Runtime.InteropServices;

namespace DesktopOrganizer.App.Native;

internal static class WindowStyleService
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExAppWindow = 0x00040000;

    public static void HideFromAltTab(nint windowHandle)
    {
        var extendedStyle = GetWindowLongPtr(windowHandle, GwlExStyle).ToInt64();
        extendedStyle = (extendedStyle | WsExToolWindow) & ~WsExAppWindow;
        SetWindowLongPtr(windowHandle, GwlExStyle, new nint(extendedStyle));
    }

    private static nint GetWindowLongPtr(nint windowHandle, int index)
    {
        return Environment.Is64BitProcess
            ? GetWindowLongPtr64(windowHandle, index)
            : new nint(GetWindowLong32(windowHandle, index));
    }

    private static nint SetWindowLongPtr(nint windowHandle, int index, nint newValue)
    {
        return Environment.Is64BitProcess
            ? SetWindowLongPtr64(windowHandle, index, newValue)
            : new nint(SetWindowLong32(windowHandle, index, newValue.ToInt32()));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(nint windowHandle, int index, int newValue);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint windowHandle, int index, nint newValue);
}
