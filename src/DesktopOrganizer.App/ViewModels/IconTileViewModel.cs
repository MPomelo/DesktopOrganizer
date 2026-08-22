using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopOrganizer.App.Models;

namespace DesktopOrganizer.App.ViewModels;

public sealed class IconTileViewModel
{
    private IconTileViewModel(
        string glyph,
        string label,
        string automationName,
        bool isMoreButton,
        bool isPlaceholder,
        ImageSource? icon,
        string? itemId)
    {
        Glyph = glyph;
        Label = label;
        AutomationName = automationName;
        IsMoreButton = isMoreButton;
        IsPlaceholder = isPlaceholder;
        Icon = icon;
        ItemId = itemId;
    }

    public string Glyph { get; }

    public string Label { get; }

    public string AutomationName { get; }

    public bool IsMoreButton { get; }

    public bool IsPlaceholder { get; }

    public ImageSource? Icon { get; }

    public bool HasIcon => Icon is not null;

    public string? ItemId { get; }

    public static IconTileViewModel CreateStoredItem(StoredIconItem item)
    {
        return new IconTileViewModel(
            GetFallbackGlyph(item.Kind),
            item.DisplayName,
            item.DisplayName,
            false,
            false,
            LoadIcon(item.IconSource),
            item.Id);
    }

    public static IconTileViewModel CreatePlaceholder(string glyph, string label, int index)
    {
        return new IconTileViewModel(
            glyph,
            label,
            $"占位图标 {index}",
            false,
            true,
            null,
            null);
    }

    public static IconTileViewModel CreateMoreButton()
    {
        return new IconTileViewModel(
            "\uE712",
            "更多",
            "显示全部图标",
            true,
            false,
            null,
            null);
    }

    private static ImageSource? LoadIcon(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException)
        {
            return null;
        }
    }

    private static string GetFallbackGlyph(IconItemKind kind)
    {
        return kind switch
        {
            IconItemKind.Folder => "\uE8B7",
            IconItemKind.File => "\uE8A5",
            IconItemKind.Shortcut or IconItemKind.App => "\uE7B8",
            IconItemKind.Url => "\uE774",
            _ => "\uE71B"
        };
    }
}
