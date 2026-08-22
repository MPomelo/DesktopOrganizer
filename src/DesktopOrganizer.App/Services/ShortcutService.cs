using System.IO;
using System.Text;
using DesktopOrganizer.App.Models;

namespace DesktopOrganizer.App.Services;

public sealed class ShortcutService
{
    public void CreateShortcut(string path, ContainerReleaseAction action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(action);

        var shellType = Type.GetTypeFromProgID("WScript.Shell", true)
            ?? throw new NotSupportedException("Windows Script Host is unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Could not create WScript.Shell.");
        dynamic shortcut = shell.CreateShortcut(path);
        shortcut.TargetPath = action.LaunchTarget;
        shortcut.Arguments = action.Arguments;
        shortcut.WorkingDirectory = action.WorkingDirectory;
        shortcut.Description = $"由 DesktopOrganizer 释放：{action.DisplayName}";
        if (!string.IsNullOrWhiteSpace(action.IconSource))
        {
            shortcut.IconLocation = action.IconSource;
        }
        shortcut.Save();
    }

    public void CreateUrl(string path, ContainerReleaseAction action)
    {
        var url = ResolveUrl(action);
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidDataException($"无法确定“{action.DisplayName}”的 URL。");
        }

        File.WriteAllText(path, $"[InternetShortcut]{Environment.NewLine}URL={url}{Environment.NewLine}", new UTF8Encoding(false));
    }

    public static string GetAvailablePath(string directory, string baseName, string extension, ISet<string>? reservedPaths = null)
    {
        var safeName = SanitizeFileName(baseName);
        for (var index = 1; ; index++)
        {
            var suffix = index == 1 ? "" : $" ({index})";
            var candidate = Path.Combine(directory, safeName + suffix + extension);
            if (!File.Exists(candidate)
                && !Directory.Exists(candidate)
                && (reservedPaths is null || !reservedPaths.Contains(candidate)))
            {
                reservedPaths?.Add(candidate);
                return candidate;
            }
        }
    }

    private static string ResolveUrl(ContainerReleaseAction action)
    {
        if (Uri.TryCreate(action.LaunchTarget, UriKind.Absolute, out var launchUri)
            && launchUri.Scheme is not "file")
        {
            return launchUri.AbsoluteUri;
        }

        if (!File.Exists(action.SourcePath))
        {
            return "";
        }

        return File.ReadLines(action.SourcePath)
            .FirstOrDefault(line => line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))?[4..]
            .Trim() ?? "";
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "DesktopOrganizer 项目" : safe;
    }
}
