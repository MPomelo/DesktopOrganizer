using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopOrganizer.App.Models;

namespace DesktopOrganizer.App.Services;

public sealed class ConfigService
{
    private const string ConfigFileName = "config.json";
    private readonly ILogger _logger;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public ConfigService(ILogger logger, string? appDirectory = null)
    {
        _logger = logger;
        var testOverrideDirectory = Environment.GetEnvironmentVariable(
            "DESKTOPORGANIZER_APPDATA");
        AppDirectory = appDirectory
            ?? (!string.IsNullOrWhiteSpace(testOverrideDirectory)
                ? testOverrideDirectory
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "DesktopOrganizer"));
        ConfigPath = Path.Combine(AppDirectory, ConfigFileName);

        _serializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        _serializerOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public string AppDirectory { get; }

    public string ConfigPath { get; }

    public async Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(AppDirectory);

        if (!File.Exists(ConfigPath))
        {
            var defaultConfig = AppConfig.CreateDefault();
            await SaveAsync(defaultConfig, cancellationToken).ConfigureAwait(false);
            _logger.Info($"Created default configuration at '{ConfigPath}'.");
            return defaultConfig;
        }

        try
        {
            AppConfig? config;
            await using (var stream = new FileStream(
                ConfigPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                config = await JsonSerializer.DeserializeAsync<AppConfig>(
                    stream,
                    _serializerOptions,
                    cancellationToken).ConfigureAwait(false);
            }

            if (config is null)
            {
                throw new InvalidDataException("The configuration file is empty.");
            }

            var wasNormalized = NormalizeAndValidate(config);
            if (wasNormalized)
            {
                await SaveAsync(config, cancellationToken).ConfigureAwait(false);
            }

            _logger.Info($"Loaded configuration from '{ConfigPath}'.");
            return config;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return await RecoverFromCorruptConfigAsync(exception, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task SaveAsync(
        AppConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        Directory.CreateDirectory(AppDirectory);

        await _saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        var tempPath = Path.Combine(AppDirectory, $"{ConfigFileName}.{Guid.NewGuid():N}.tmp");
        var backupPath = Path.Combine(AppDirectory, $"{ConfigFileName}.previous");

        try
        {
            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(config, _serializerOptions);
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(jsonBytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(ConfigPath))
            {
                TryDeleteFile(backupPath);
                File.Replace(tempPath, ConfigPath, backupPath, true);
                TryDeleteFile(backupPath);
            }
            else
            {
                File.Move(tempPath, ConfigPath);
            }

            _logger.Info($"Saved configuration to '{ConfigPath}'.");
        }
        finally
        {
            TryDeleteFile(tempPath);
            _saveLock.Release();
        }
    }

    private async Task<AppConfig> RecoverFromCorruptConfigAsync(
        Exception exception,
        CancellationToken cancellationToken)
    {
        var badConfigPath = GetAvailableBadConfigPath();
        File.Move(ConfigPath, badConfigPath);
        _logger.Error(
            $"Configuration was invalid and has been moved to '{badConfigPath}'.",
            exception);

        var defaultConfig = AppConfig.CreateDefault();
        await SaveAsync(defaultConfig, cancellationToken).ConfigureAwait(false);
        return defaultConfig;
    }

    private static bool NormalizeAndValidate(AppConfig config)
    {
        if (config.SchemaVersion > AppConfig.CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"Configuration schema {config.SchemaVersion} is newer than supported schema " +
                $"{AppConfig.CurrentSchemaVersion}.");
        }

        var changed = false;
        if (config.SchemaVersion <= 0)
        {
            config.SchemaVersion = AppConfig.CurrentSchemaVersion;
            changed = true;
        }

        if (config.Containers is null)
        {
            config.Containers = [];
            changed = true;
        }

        if (config.Containers.Count == 0 && config.Behavior?.HasClosedAllContainers != true)
        {
            config.Containers.Add(ContainerConfig.CreateDefault());
            changed = true;
        }

        foreach (var container in config.Containers)
        {
            changed |= NormalizeContainer(container);
        }

        if (config.Theme is null)
        {
            config.Theme = new ThemeConfig();
            changed = true;
        }

        if (config.Behavior is null)
        {
            config.Behavior = new BehaviorConfig();
            changed = true;
        }

        return changed;
    }

    private static bool NormalizeContainer(ContainerConfig container)
    {
        var changed = false;

        if (string.IsNullOrWhiteSpace(container.Id))
        {
            container.Id = Guid.NewGuid().ToString("N");
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(container.Name))
        {
            container.Name = "收纳区";
            changed = true;
        }

        if (!Enum.IsDefined(container.SizeMode))
        {
            container.SizeMode = ContainerSizeMode.TwoByTwo;
            changed = true;
        }

        if (!Enum.IsDefined(container.Orientation))
        {
            container.Orientation = ContainerOrientation.Horizontal;
            changed = true;
        }

        if (!double.IsFinite(container.Left) || !double.IsFinite(container.Top))
        {
            container.Left = 0;
            container.Top = 0;
            container.HasSavedPosition = false;
            changed = true;
        }

        if (container.Items is null)
        {
            container.Items = [];
            changed = true;
        }

        if (container.PinnedIconIds is null)
        {
            container.PinnedIconIds = [];
            changed = true;
        }

        if (container.Style is null)
        {
            container.Style = new ContainerStyleConfig();
            changed = true;
        }

        changed |= NormalizeStyle(container.Style);

        return changed;
    }

    private static bool NormalizeStyle(ContainerStyleConfig style)
    {
        var changed = false;
        if (!double.IsFinite(style.BorderThickness) || style.BorderThickness is < 1 or > 12)
        {
            style.BorderThickness = 4;
            changed = true;
        }

        if (!double.IsFinite(style.BackgroundOpacity) || style.BackgroundOpacity is < 0.15 or > 0.9)
        {
            style.BackgroundOpacity = 0.45;
            changed = true;
        }

        changed |= NormalizeColorSetting(
            style.BorderColor,
            ThemeColorMode.SystemAccent,
            setting => style.BorderColor = setting);
        changed |= NormalizeColorSetting(
            style.BackgroundColor,
            ThemeColorMode.SystemAccent,
            setting => style.BackgroundColor = setting);
        changed |= NormalizeColorSetting(
            style.MoreButtonColor,
            ThemeColorMode.Auto,
            setting => style.MoreButtonColor = setting);
        changed |= NormalizeColorSetting(
            style.HoverBackgroundColor,
            ThemeColorMode.Auto,
            setting => style.HoverBackgroundColor = setting);
        return changed;
    }

    private static bool NormalizeColorSetting(
        ThemeColorSetting? setting,
        ThemeColorMode defaultMode,
        Action<ThemeColorSetting> replace)
    {
        if (setting is null || !Enum.IsDefined(setting.Mode))
        {
            replace(new ThemeColorSetting { Mode = defaultMode });
            return true;
        }

        if (setting.Mode != ThemeColorMode.Custom)
        {
            return false;
        }

        if (!ThemeService.TryNormalizeHexColor(setting.HexColor, out var normalized))
        {
            setting.Mode = defaultMode;
            setting.HexColor = null;
            return true;
        }

        if (string.Equals(setting.HexColor, normalized, StringComparison.Ordinal))
        {
            return false;
        }

        setting.HexColor = normalized;
        return true;
    }

    private string GetAvailableBadConfigPath()
    {
        var basePath = Path.Combine(AppDirectory, "config.bad.json");
        if (!File.Exists(basePath))
        {
            return basePath;
        }

        return Path.Combine(
            AppDirectory,
            $"config.bad-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.json");
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
