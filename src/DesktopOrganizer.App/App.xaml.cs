using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopOrganizer.App.Models;
using DesktopOrganizer.App.Services;
using DesktopOrganizer.App.ViewModels;
using DesktopOrganizer.App.Views;

namespace DesktopOrganizer.App;

public partial class App : System.Windows.Application
{
    private ILogger? _logger;
    private ConfigService? _configService;
    private ThemeService? _themeService;
    private ContainerReleaseService? _releaseService;
    private DragDropImportService? _dragDropImportService;
    private IIconLaunchService? _iconLaunchService;
    private AppConfig? _appConfig;
    private ContainerConfig? _containerConfig;
    private TrayService? _trayService;
    private ContainerWindow? _containerWindow;
    private ExpandedContainerWindow? _expandedWindow;
    private SettingsWindow? _settingsWindow;
    private ContainerViewModel? _containerViewModel;
    private bool _isShuttingDown;
    private bool _isReleasing;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _logger = new FileLogger();
        RegisterExceptionHandlers();

        try
        {
            _logger.Info("Application starting.");
            _configService = new ConfigService(_logger);
            _themeService = new ThemeService();
            _appConfig = await _configService.LoadAsync();
            _releaseService = new ContainerReleaseService(
                _configService.AppDirectory,
                new DesktopCapacityService(),
                new ShortcutService(),
                _logger);
            await _releaseService.RecoverIfNeededAsync(_appConfig);

            var iconExtractionService = new IconExtractionService(
                Path.Combine(_configService.AppDirectory, "IconCache"),
                _logger);
            _dragDropImportService = new DragDropImportService(
                _configService.AppDirectory,
                iconExtractionService,
                new ShellLinkParser(),
                _logger);
            _iconLaunchService = new IconLaunchService(_logger);

            _trayService = new TrayService(
                Dispatcher,
                ShowOrCreateContainer,
                HideContainer,
                ShutdownApplication,
                _logger);
            _trayService.Show();

            if (_appConfig.Containers.Count == 0)
            {
                _appConfig.Containers.Add(ContainerConfig.CreateDefault());
                _appConfig.Behavior.HasClosedAllContainers = false;
                await _configService.SaveAsync(_appConfig);
            }

            CreateAndShowContainer(_appConfig.Containers[0]);
            if (!string.Equals(
                Environment.GetEnvironmentVariable("DESKTOPORGANIZER_SKIP_STARTUP_SETTINGS"),
                "1",
                StringComparison.Ordinal))
            {
                _ = Dispatcher.BeginInvoke(
                    () => OnSettingsRequested(this, EventArgs.Empty),
                    DispatcherPriority.ApplicationIdle);
            }

            _logger.Info("Stage 7 release workflow is ready.");
        }
        catch (Exception exception)
        {
            _logger.Error("Application startup failed.", exception);
            System.Windows.MessageBox.Show(
                "DesktopOrganizer 启动失败。请查看应用日志；若存在 release-journal.json，请保留该文件用于恢复。\n\n" + exception.Message,
                "DesktopOrganizer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            ShutdownApplication();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SaveFinalConfiguration();
        _trayService?.Dispose();
        _logger?.Info("Application stopped.");
        base.OnExit(e);
    }

    private void RegisterExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.Error("Unhandled UI exception.", e.Exception);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        _logger?.Error("Unhandled application exception.", e.ExceptionObject as Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger?.Error("Unobserved task exception.", e.Exception);
        e.SetObserved();
    }

    private void CreateAndShowContainer(ContainerConfig containerConfig)
    {
        if (_appConfig is null || _dragDropImportService is null || _iconLaunchService is null
            || _themeService is null || _logger is null)
        {
            throw new InvalidOperationException("Container services are not initialized.");
        }

        _containerConfig = containerConfig;
        _containerViewModel = new ContainerViewModel(
            new DesktopGridService(),
            containerConfig,
            _appConfig.Behavior,
            _dragDropImportService,
            _iconLaunchService,
            _logger,
            PersistConfigurationAsync,
            _themeService);
        _containerViewModel.ConfigurationChanged += OnContainerConfigurationChanged;

        _containerWindow = new ContainerWindow(_containerViewModel);
        _containerWindow.CloseRequested += OnContainerCloseRequested;
        _containerWindow.HideRequested += OnContainerHideRequested;
        _containerWindow.ExpandedRequested += OnExpandedRequested;
        _containerWindow.SettingsRequested += OnSettingsRequested;
        _containerWindow.Closed += OnContainerClosed;
        _containerWindow.Loaded += OnContainerLoaded;
        _containerWindow.PositionChanged += OnContainerPositionChanged;
        ApplySavedWindowPosition(_containerWindow, containerConfig);
        _containerWindow.Show();
        _trayService?.UpdateContainerState(true, true);
    }

    private async void ShowOrCreateContainer()
    {
        if (_isShuttingDown || _appConfig is null)
        {
            return;
        }

        if (_containerWindow is not null)
        {
            _containerWindow.Show();
            _containerWindow.Activate();
            _trayService?.UpdateContainerState(true, true);
            return;
        }

        var container = _appConfig.Containers.FirstOrDefault();
        if (container is null)
        {
            container = ContainerConfig.CreateDefault();
            _appConfig.Containers.Add(container);
            _appConfig.Behavior.HasClosedAllContainers = false;
            try
            {
                await PersistConfigurationAsync();
            }
            catch (Exception exception)
            {
                _appConfig.Containers.Remove(container);
                _appConfig.Behavior.HasClosedAllContainers = true;
                _logger?.Error("Failed to create a new container.", exception);
                System.Windows.MessageBox.Show(
                    $"无法创建新的收纳区。\n\n{exception.Message}",
                    "创建失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }
        }

        CreateAndShowContainer(container);
    }

    private void HideContainer()
    {
        if (_containerWindow is null)
        {
            return;
        }

        _settingsWindow?.Close();
        _expandedWindow?.Close();
        _containerWindow.Hide();
        _trayService?.UpdateContainerState(true, false);
        _logger?.Info("Container hidden without changing configuration or icon files.");
    }

    private void OnContainerHideRequested(object? sender, EventArgs e) => HideContainer();

    private void OnContainerCloseRequested(object? sender, EventArgs e)
    {
        _ = ReleaseContainerAsync(false);
    }

    private async Task ReleaseContainerAsync(bool shutdownAfterRelease)
    {
        if (_isReleasing || _containerConfig is null || _releaseService is null
            || _appConfig is null || _containerWindow is null)
        {
            return;
        }

        _isReleasing = true;
        try
        {
            var plan = _releaseService.CreatePlan(_containerConfig);
            if (plan.TotalCount > 0)
            {
                var confirmation = new ReleaseConfirmationWindow(plan)
                {
                    Owner = _containerWindow
                };
                if (confirmation.ShowDialog() != true)
                {
                    _logger?.Info("Container release was cancelled before file operations started.");
                    return;
                }

                plan = _releaseService.CreatePlan(_containerConfig, confirmation.SelectedDesktopCount);
            }
            else if (shutdownAfterRelease)
            {
                var confirmation = System.Windows.MessageBox.Show(
                    _containerWindow,
                    "这会彻底退出 DesktopOrganizer 并删除当前空收纳区。\n\n" +
                    "以后再次启动软件时会创建一个新的空收纳区。是否继续？",
                    "彻底退出收纳区",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);
                if (confirmation != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            var result = await _releaseService.ExecuteAsync(
                _appConfig,
                plan,
                PersistConfigurationAsync);
            CloseReleasedContainerWindow();
            _trayService?.UpdateContainerState(_appConfig.Containers.Count > 0, false);

            if (plan.TotalCount > 0)
            {
                System.Windows.MessageBox.Show(
                    $"已关闭收纳区：{result.DesktopCount} 个图标已释放到桌面，" +
                    $"{result.StartMenuCount} 个已添加到开始菜单，" +
                    $"{result.NoActionCount} 个桌面引用无需重复创建。\n\n" +
                    $"开始菜单目录：{result.StartMenuDirectory}",
                    "释放完成",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            if (shutdownAfterRelease)
            {
                ShutdownApplication();
            }
        }
        catch (Exception exception)
        {
            _logger?.Error("Container release failed.", exception);
            var journalPath = _releaseService.JournalPath;
            System.Windows.MessageBox.Show(
                "关闭未完成，收纳区配置已保留，已执行的文件操作已尝试回滚。\n\n" +
                $"{exception.Message}\n\n恢复日志：{journalPath}",
                "释放失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isReleasing = false;
        }
    }

    private void CloseReleasedContainerWindow()
    {
        _settingsWindow?.Close();
        _expandedWindow?.Close();
        _containerWindow?.CloseWithoutRequest();
    }

    private void OnExpandedRequested(object? sender, EventArgs e)
    {
        if (_containerWindow?.DataContext is not ContainerViewModel viewModel)
        {
            return;
        }

        if (_expandedWindow is not null)
        {
            _expandedWindow.Activate();
            return;
        }

        var expandedWindow = new ExpandedContainerWindow(viewModel) { Owner = _containerWindow };
        expandedWindow.Closed += OnExpandedWindowClosed;
        _expandedWindow = expandedWindow;
        expandedWindow.Show();
        expandedWindow.Activate();
        _logger?.Info($"Opened expanded container with {viewModel.StoredItemCount} icon items.");
    }

    private void OnSettingsRequested(object? sender, EventArgs e)
    {
        if (_containerWindow is null || _containerViewModel is null)
        {
            return;
        }

        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _expandedWindow?.Close();
        var fullExitRequested = false;
        try
        {
            var settingsWindow = new SettingsWindow(new SettingsViewModel(_containerViewModel))
            {
                Owner = _containerWindow
            };
            _settingsWindow = settingsWindow;
            _logger?.Info("Opened container settings.");
            settingsWindow.ShowDialog();
            fullExitRequested = settingsWindow.FullExitRequested;
        }
        finally
        {
            _containerViewModel.CancelSettingsPreview();
            _settingsWindow = null;
            _logger?.Info("Container settings closed.");
        }

        if (fullExitRequested)
        {
            _ = ReleaseContainerAsync(true);
        }
    }

    private void OnExpandedWindowClosed(object? sender, EventArgs e)
    {
        if (sender is ExpandedContainerWindow expandedWindow)
        {
            expandedWindow.Closed -= OnExpandedWindowClosed;
        }

        _expandedWindow = null;
        _logger?.Info("Expanded container closed.");
    }

    private void OnContainerLoaded(object sender, RoutedEventArgs e)
    {
        if (_containerWindow is null)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(_containerWindow);
        _logger?.Info(
            $"Container loaded at {_containerWindow.ActualWidth:0.##}x{_containerWindow.ActualHeight:0.##} DIPs, " +
            $"DPI scale {dpi.DpiScaleX:0.##}x{dpi.DpiScaleY:0.##}.");
        if (_containerConfig is not null && !_containerConfig.HasSavedPosition)
        {
            UpdateContainerPosition(
                _containerWindow.Left,
                _containerWindow.Top,
                _containerWindow.GetCurrentMonitorDeviceName());
            _ = SaveConfigurationAsync("initial window position");
        }
    }

    private void OnContainerConfigurationChanged(object? sender, EventArgs e)
    {
        _ = SaveConfigurationAsync("container layout");
    }

    private void OnContainerPositionChanged(object? sender, WindowPositionChangedEventArgs e)
    {
        UpdateContainerPosition(e.Left, e.Top, e.MonitorDeviceName);
        _ = SaveConfigurationAsync("container position");
    }

    private void OnContainerClosed(object? sender, EventArgs e)
    {
        if (sender is ContainerWindow window)
        {
            window.CloseRequested -= OnContainerCloseRequested;
            window.HideRequested -= OnContainerHideRequested;
            window.ExpandedRequested -= OnExpandedRequested;
            window.SettingsRequested -= OnSettingsRequested;
            window.Closed -= OnContainerClosed;
            window.Loaded -= OnContainerLoaded;
            window.PositionChanged -= OnContainerPositionChanged;
        }

        if (_containerViewModel is not null)
        {
            _containerViewModel.ConfigurationChanged -= OnContainerConfigurationChanged;
        }

        _containerWindow = null;
        _containerViewModel = null;
        _containerConfig = null;
        if (!_isShuttingDown)
        {
            _trayService?.UpdateContainerState(_appConfig?.Containers.Count > 0, false);
        }
    }

    private static void ApplySavedWindowPosition(Window window, ContainerConfig containerConfig)
    {
        if (!containerConfig.HasSavedPosition
            || !double.IsFinite(containerConfig.Left)
            || !double.IsFinite(containerConfig.Top))
        {
            return;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = containerConfig.Left;
        window.Top = containerConfig.Top;
    }

    private void UpdateContainerPosition(double left, double top, string monitorDeviceName)
    {
        if (_containerConfig is null)
        {
            return;
        }

        _containerConfig.Left = left;
        _containerConfig.Top = top;
        _containerConfig.MonitorDeviceName = monitorDeviceName;
        _containerConfig.HasSavedPosition = true;
    }

    private async Task SaveConfigurationAsync(string reason)
    {
        if (_configService is null || _appConfig is null)
        {
            return;
        }

        if (_containerViewModel?.HasActiveSettingsPreview == true)
        {
            _logger?.Info($"Deferred {reason} while settings preview is active.");
            return;
        }

        try
        {
            await _configService.SaveAsync(_appConfig);
            _logger?.Info($"Persisted {reason}.");
        }
        catch (Exception exception)
        {
            _logger?.Error($"Failed to persist {reason}.", exception);
        }
    }

    private Task PersistConfigurationAsync()
    {
        if (_configService is null || _appConfig is null)
        {
            throw new InvalidOperationException("Configuration services are not initialized.");
        }

        return _configService.SaveAsync(_appConfig);
    }

    private void SaveFinalConfiguration()
    {
        if (_configService is null || _appConfig is null)
        {
            return;
        }

        try
        {
            _containerViewModel?.CancelSettingsPreview();
            _configService.SaveAsync(_appConfig).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            _logger?.Error("Failed to persist final application configuration.", exception);
        }
    }

    private void ShutdownApplication()
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;
        _logger?.Info("Application shutdown requested; container configuration is retained.");
        _settingsWindow?.Close();
        _expandedWindow?.Close();
        _containerWindow?.CloseWithoutRequest();
        Shutdown();
    }
}
