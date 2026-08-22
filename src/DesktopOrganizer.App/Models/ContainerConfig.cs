namespace DesktopOrganizer.App.Models;

public sealed class ContainerConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "收纳区";

    public ContainerSizeMode SizeMode { get; set; } = ContainerSizeMode.TwoByTwo;

    public ContainerOrientation Orientation { get; set; } = ContainerOrientation.Horizontal;

    public double Left { get; set; }

    public double Top { get; set; }

    public bool HasSavedPosition { get; set; }

    public string MonitorDeviceName { get; set; } = "";

    public List<StoredIconItem> Items { get; set; } = [];

    public List<string> PinnedIconIds { get; set; } = [];

    public ContainerStyleConfig Style { get; set; } = new();

    public static ContainerConfig CreateDefault() => new();
}
