using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using DesktopOrganizer.App.Models;

namespace DesktopOrganizer.App.Services;

public sealed class IconLaunchService(ILogger logger) : IIconLaunchService
{
    private readonly ILogger _logger = logger;

    public Task LaunchAsync(
        StoredIconItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var startInfo = CreateStartInfo(item);
            using var process = Process.Start(startInfo);
            _logger.Info(
                $"Opened icon item '{item.DisplayName}' ({item.Id}) using '{startInfo.FileName}'.");
            return Task.CompletedTask;
        }
        catch (Exception exception) when (exception is
            Win32Exception or
            FileNotFoundException or
            DirectoryNotFoundException or
            InvalidOperationException or
            UnauthorizedAccessException)
        {
            _logger.Error(
                $"Failed to open icon item '{item.DisplayName}' ({item.Id}).",
                exception);
            throw;
        }
    }

    internal static ProcessStartInfo CreateStartInfo(StoredIconItem item)
    {
        var target = ResolveLaunchTarget(item);
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new InvalidOperationException("The icon item has no launch target.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = target,
            UseShellExecute = true,
            ErrorDialog = false
        };

        if (!string.IsNullOrWhiteSpace(item.Arguments))
        {
            startInfo.Arguments = item.Arguments;
        }

        if (!string.IsNullOrWhiteSpace(item.WorkingDirectory)
            && Directory.Exists(item.WorkingDirectory))
        {
            startInfo.WorkingDirectory = item.WorkingDirectory;
        }

        return startInfo;
    }

    private static string ResolveLaunchTarget(StoredIconItem item)
    {
        return item.Kind switch
        {
            IconItemKind.Shortcut or IconItemKind.App or IconItemKind.Url or IconItemKind.ShellItem
                when !string.IsNullOrWhiteSpace(item.LaunchTarget) => item.LaunchTarget,
            _ when !string.IsNullOrWhiteSpace(item.SourcePath) => item.SourcePath,
            _ => item.LaunchTarget
        };
    }
}
