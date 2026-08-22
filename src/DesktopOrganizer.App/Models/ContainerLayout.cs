namespace DesktopOrganizer.App.Models;

public sealed record ContainerLayout(
    int Rows,
    int Columns,
    double Width,
    double Height);
