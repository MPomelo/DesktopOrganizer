using System.Globalization;
using System.IO;
using System.Text;

namespace DesktopOrganizer.App.Services;

public sealed class FileLogger : ILogger
{
    private readonly object _writeLock = new();
    private readonly string _logPath;

    public FileLogger()
    {
        var overrideDirectory = Environment.GetEnvironmentVariable("DESKTOPORGANIZER_APPDATA");
        var appDirectory = !string.IsNullOrWhiteSpace(overrideDirectory)
            ? overrideDirectory
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DesktopOrganizer");
        var logDirectory = Path.Combine(appDirectory, "Logs");
        Directory.CreateDirectory(logDirectory);
        _logPath = Path.Combine(logDirectory, "app.log");
    }

    public void Info(string message) => Write("INF", message, null);

    public void Error(string message, Exception? exception = null) => Write("ERR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
        var line = $"{timestamp} [{level}] {message}";

        if (exception is not null)
        {
            line += Environment.NewLine + exception;
        }

        lock (_writeLock)
        {
            File.AppendAllText(_logPath, line + Environment.NewLine, Encoding.UTF8);
        }
    }
}
