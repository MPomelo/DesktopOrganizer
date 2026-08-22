using DesktopOrganizer.App.Models;

namespace DesktopOrganizer.App.Services;

public static class ContainerLayoutCalculator
{
    public static ContainerLayout Calculate(
        ContainerSizeMode sizeMode,
        ContainerOrientation orientation,
        DesktopGridMetrics gridMetrics)
    {
        var (rows, columns) = sizeMode switch
        {
            ContainerSizeMode.TwoByTwo => (Rows: 2, Columns: 2),
            ContainerSizeMode.OneByFour when orientation == ContainerOrientation.Horizontal =>
                (Rows: 1, Columns: 4),
            ContainerSizeMode.OneByFour => (Rows: 4, Columns: 1),
            ContainerSizeMode.TwoByFour when orientation == ContainerOrientation.Horizontal =>
                (Rows: 2, Columns: 4),
            ContainerSizeMode.TwoByFour => (Rows: 4, Columns: 2),
            _ => throw new ArgumentOutOfRangeException(nameof(sizeMode), sizeMode, null)
        };

        return new ContainerLayout(
            rows,
            columns,
            columns * gridMetrics.CellWidth,
            rows * gridMetrics.CellHeight);
    }
}
