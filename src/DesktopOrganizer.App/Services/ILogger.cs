namespace DesktopOrganizer.App.Services;

public interface ILogger
{
    void Info(string message);

    void Error(string message, Exception? exception = null);
}
