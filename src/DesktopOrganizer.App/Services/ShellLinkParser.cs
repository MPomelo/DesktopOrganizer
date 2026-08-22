using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using DesktopOrganizer.App.Models;

namespace DesktopOrganizer.App.Services;

public sealed class ShellLinkParser
{
    private const int MaxPath = 32768;
    private const uint SlgpRawPath = 0x0004;
    private static readonly Guid ShellLinkClassId = new("00021401-0000-0000-C000-000000000046");

    public ShellLinkInfo Parse(string shortcutPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcutPath);

        var shellLinkType = Type.GetTypeFromCLSID(ShellLinkClassId, true)
            ?? throw new NotSupportedException("Windows Shell Link COM is unavailable.");
        var shellLink = (IShellLinkW)(Activator.CreateInstance(shellLinkType)
            ?? throw new InvalidOperationException("Could not create a Windows Shell Link instance."));
        try
        {
            ((IPersistFile)shellLink).Load(shortcutPath, 0);

            var targetPath = new StringBuilder(MaxPath);
            shellLink.GetPath(targetPath, targetPath.Capacity, nint.Zero, SlgpRawPath);

            var arguments = new StringBuilder(MaxPath);
            shellLink.GetArguments(arguments, arguments.Capacity);

            var workingDirectory = new StringBuilder(MaxPath);
            shellLink.GetWorkingDirectory(workingDirectory, workingDirectory.Capacity);

            var iconPath = new StringBuilder(MaxPath);
            shellLink.GetIconLocation(iconPath, iconPath.Capacity, out var iconIndex);

            var description = new StringBuilder(1024);
            shellLink.GetDescription(description, description.Capacity);

            return new ShellLinkInfo(
                Environment.ExpandEnvironmentVariables(targetPath.ToString()),
                arguments.ToString(),
                Environment.ExpandEnvironmentVariables(workingDirectory.ToString()),
                Environment.ExpandEnvironmentVariables(iconPath.ToString()),
                iconIndex,
                description.ToString());
        }
        finally
        {
            if (Marshal.IsComObject(shellLink))
            {
                Marshal.FinalReleaseComObject(shellLink);
            }
        }
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file,
            int maxPath,
            nint findData,
            uint flags);

        void GetIDList(out nint itemIdList);

        void SetIDList(nint itemIdList);

        void GetDescription(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name,
            int maxName);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetWorkingDirectory(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory,
            int maxPath);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);

        void GetArguments(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments,
            int maxPath);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);

        void GetHotkey(out short hotkey);

        void SetHotkey(short hotkey);

        void GetShowCmd(out int showCommand);

        void SetShowCmd(int showCommand);

        void GetIconLocation(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath,
            int iconPathLength,
            out int iconIndex);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);

        void Resolve(nint windowHandle, uint flags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
