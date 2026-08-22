using DesktopOrganizer.App.Models;

namespace DesktopOrganizer.App.Services;

public interface IIconLaunchService
{
    Task LaunchAsync(
        StoredIconItem item,
        CancellationToken cancellationToken = default);
}
