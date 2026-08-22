using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopOrganizer.App.Services;

public sealed class IconExtractionService
{
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;
    private readonly string _cacheDirectory;
    private readonly ILogger _logger;

    public IconExtractionService(string cacheDirectory, ILogger logger)
    {
        _cacheDirectory = cacheDirectory;
        _logger = logger;
    }

    public Task<string?> ExtractAndCacheAsync(
        string sourcePath,
        string itemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        return Task.Run(() => ExtractAndCache(sourcePath, itemId, cancellationToken), cancellationToken);
    }

    private string? ExtractAndCache(
        string sourcePath,
        string itemId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_cacheDirectory);

        var cachePath = Path.Combine(_cacheDirectory, $"{itemId}.png");
        if (File.Exists(cachePath))
        {
            return cachePath;
        }

        var result = SHGetFileInfo(
            sourcePath,
            0,
            out var fileInfo,
            (uint)Marshal.SizeOf<SHFileInfo>(),
            ShgfiIcon | ShgfiLargeIcon);

        if (result == nint.Zero || fileInfo.IconHandle == nint.Zero)
        {
            _logger.Error($"Windows Shell did not return an icon for '{sourcePath}'.");
            return null;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                fileInfo.IconHandle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(48, 48));
            bitmapSource.Freeze();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));

            using var stream = new FileStream(cachePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            encoder.Save(stream);
            return cachePath;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException)
        {
            TryDelete(cachePath);
            _logger.Error($"Failed to cache the icon for '{sourcePath}'.", exception);
            return null;
        }
        finally
        {
            DestroyIcon(fileInfo.IconHandle);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFileInfo
    {
        public nint IconHandle;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(
        string path,
        uint fileAttributes,
        out SHFileInfo fileInfo,
        uint fileInfoSize,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint iconHandle);
}
