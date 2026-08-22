using DesktopOrganizer.App.Models;

namespace DesktopOrganizer.App.ViewModels;

public sealed class ReleaseConfirmationViewModel : ViewModelBase
{
    private int _desktopCount;

    public ReleaseConfirmationViewModel(ContainerReleasePlan plan)
    {
        Plan = plan;
        _desktopCount = plan.DesktopCount;
    }

    public ContainerReleasePlan Plan { get; }

    public int TotalCount => Plan.TotalCount;

    public int NoActionCount => Plan.NoActionCount;

    public int MaximumDesktopCount => Plan.MaximumDesktopCount;

    public string StartMenuDirectory => Plan.StartMenuDirectory;

    public string CapacityExplanation => Plan.CapacityExplanation;

    public int DesktopCount
    {
        get => _desktopCount;
        set
        {
            var safeValue = Math.Clamp(value, 0, MaximumDesktopCount);
            if (SetProperty(ref _desktopCount, safeValue))
            {
                OnPropertyChanged(nameof(StartMenuCount));
            }
        }
    }

    public int StartMenuCount => TotalCount - NoActionCount - DesktopCount;
}
