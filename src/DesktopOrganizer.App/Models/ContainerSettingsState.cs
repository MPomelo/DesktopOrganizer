namespace DesktopOrganizer.App.Models;

public sealed class ContainerSettingsState
{
    public string Name { get; set; } = "收纳区";

    public ContainerSizeMode SizeMode { get; set; } = ContainerSizeMode.TwoByTwo;

    public ContainerOrientation Orientation { get; set; } = ContainerOrientation.Horizontal;

    public List<StoredIconItem> Items { get; set; } = [];

    public List<string> PinnedIconIds { get; set; } = [];

    public ContainerStyleConfig Style { get; set; } = new();

    public static ContainerSettingsState Capture(ContainerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new ContainerSettingsState
        {
            Name = config.Name,
            SizeMode = config.SizeMode,
            Orientation = config.Orientation,
            Items = config.Items.Select(CloneItem).ToList(),
            PinnedIconIds = [.. config.PinnedIconIds],
            Style = CloneStyle(config.Style)
        };
    }

    public ContainerSettingsState Clone()
    {
        return new ContainerSettingsState
        {
            Name = Name,
            SizeMode = SizeMode,
            Orientation = Orientation,
            Items = Items.Select(CloneItem).ToList(),
            PinnedIconIds = [.. PinnedIconIds],
            Style = CloneStyle(Style)
        };
    }

    public void ApplyTo(ContainerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Name = string.IsNullOrWhiteSpace(Name) ? "收纳区" : Name.Trim();
        config.SizeMode = SizeMode;
        config.Orientation = Orientation;
        config.Items = Items.Select(CloneItem).ToList();
        config.PinnedIconIds = [.. PinnedIconIds];
        config.Style = CloneStyle(Style);
    }

    public static StoredIconItem CloneItem(StoredIconItem item)
    {
        return new StoredIconItem
        {
            Id = item.Id,
            DisplayName = item.DisplayName,
            Kind = item.Kind,
            SourcePath = item.SourcePath,
            LaunchTarget = item.LaunchTarget,
            Arguments = item.Arguments,
            WorkingDirectory = item.WorkingDirectory,
            IconSource = item.IconSource,
            StorageMode = item.StorageMode,
            OriginalPath = item.OriginalPath,
            ManagedPath = item.ManagedPath,
            AddedAt = item.AddedAt,
            LastLaunchedAt = item.LastLaunchedAt
        };
    }

    public static ContainerStyleConfig CloneStyle(ContainerStyleConfig style)
    {
        return new ContainerStyleConfig
        {
            BorderThickness = style.BorderThickness,
            BackgroundOpacity = style.BackgroundOpacity,
            BorderColor = CloneColor(style.BorderColor),
            BackgroundColor = CloneColor(style.BackgroundColor),
            MoreButtonColor = CloneColor(style.MoreButtonColor),
            HoverBackgroundColor = CloneColor(style.HoverBackgroundColor)
        };
    }

    public static ThemeColorSetting CloneColor(ThemeColorSetting setting)
    {
        return new ThemeColorSetting
        {
            Mode = setting.Mode,
            HexColor = setting.HexColor
        };
    }
}
