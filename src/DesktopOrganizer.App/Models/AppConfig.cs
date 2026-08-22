namespace DesktopOrganizer.App.Models;

public sealed class AppConfig
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<ContainerConfig> Containers { get; set; } = [];

    public ThemeConfig Theme { get; set; } = new();

    public BehaviorConfig Behavior { get; set; } = new();

    public static AppConfig CreateDefault()
    {
        return new AppConfig
        {
            Containers = [ContainerConfig.CreateDefault()]
        };
    }
}

public sealed class ThemeConfig
{
}

public sealed class BehaviorConfig
{
    public bool HasChosenDesktopShortcutImportBehavior { get; set; }

    public bool MoveDesktopShortcutsToManagedStorage { get; set; } = true;

    public bool HasClosedAllContainers { get; set; }
}
