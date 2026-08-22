using Color = System.Windows.Media.Color;

namespace DesktopOrganizer.App.Models;

public sealed record ResolvedContainerTheme(
    Color BorderColor,
    Color BackgroundColor,
    Color MoreButtonColor,
    Color HoverBackgroundColor);
