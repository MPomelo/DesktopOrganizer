using System.IO;
using System.Windows;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopOrganizer.App.Models;
using DesktopOrganizer.App.Native;
using DesktopOrganizer.App.ViewModels;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using Forms = System.Windows.Forms;
using IDataObject = System.Windows.IDataObject;
using WpfButton = System.Windows.Controls.Button;

namespace DesktopOrganizer.App.Views;

public partial class ContainerWindow : Window
{
    private readonly ContainerViewModel _viewModel;
    private readonly DispatcherTimer _statusTimer;
    private bool _isImporting;
    private string? _lastLaunchedItemId;
    private DateTimeOffset _lastLaunchClickAt;
    private bool _allowClose;

    public event EventHandler? CloseRequested;

    public event EventHandler? ExpandedRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? HideRequested;

    public event EventHandler<WindowPositionChangedEventArgs>? PositionChanged;

    public ContainerWindow(ContainerViewModel viewModel)
    {
        _viewModel = viewModel;
        _statusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _statusTimer.Tick += OnStatusTimerTick;
        DataContext = viewModel;
        InitializeComponent();
        ApplyWindowSize();

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SourceInitialized += OnSourceInitialized;
        Closed += OnWindowClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        WindowStyleService.HideFromAltTab(handle);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ContainerViewModel.WindowWidth)
            or nameof(ContainerViewModel.WindowHeight))
        {
            ApplyWindowSize();
        }
    }

    private void ApplyWindowSize()
    {
        Width = _viewModel.WindowWidth;
        Height = _viewModel.WindowHeight;
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _statusTimer.Stop();
        _statusTimer.Tick -= OnStatusTimerTick;
    }

    public void CloseWithoutRequest()
    {
        _allowClose = true;
        Close();
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        RequestClose();
    }

    private void OnContainerMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
            PositionChanged?.Invoke(
                this,
                new WindowPositionChangedEventArgs(Left, Top, GetCurrentMonitorDeviceName()));
        }
    }

    public string GetCurrentMonitorDeviceName()
    {
        var handle = new WindowInteropHelper(this).Handle;
        return Forms.Screen.FromHandle(handle).DeviceName;
    }

    private void OnWindowPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var clickedButton = FindVisualAncestor<WpfButton>(e.OriginalSource as DependencyObject);
        if (clickedButton?.DataContext is IconTileViewModel { IsMoreButton: true })
        {
            SettingsRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (FindResource("ContainerContextMenu") is not ContextMenu contextMenu)
        {
            return;
        }

        contextMenu.PlacementTarget = this;
        contextMenu.IsOpen = true;
        e.Handled = true;
    }

    private void OnPreviewDragEnter(object sender, DragEventArgs e)
    {
        UpdateDragState(e);
    }

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        UpdateDragState(e);
    }

    private void OnPreviewDragLeave(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        e.Handled = true;

        if (_isImporting || !TryGetDroppedPaths(e.Data, out var paths))
        {
            return;
        }

        if (_viewModel.NeedsDesktopShortcutImportChoice(paths))
        {
            var choice = System.Windows.MessageBox.Show(
                this,
                "检测到桌面快捷方式。\n\n" +
                "“是”：移动到 DesktopOrganizer 托管目录，桌面不再重复显示。\n" +
                "“否”：只保存引用，原快捷方式留在桌面。\n" +
                "“取消”：停止本次导入。",
                "如何收纳桌面快捷方式",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question,
                MessageBoxResult.Cancel);

            if (choice == MessageBoxResult.Cancel)
            {
                return;
            }

            _viewModel.SetDesktopShortcutImportBehavior(choice == MessageBoxResult.Yes);
        }

        _isImporting = true;
        ShowStatus("正在提取图标...");

        try
        {
            var result = await _viewModel.ImportFilesAsync(paths);
            var status = result.ImportedCount > 0
                ? $"已添加 {result.ImportedCount} 个项目"
                : "没有添加项目";
            ShowStatus(status);

            if (result.Failures.Count > 0)
            {
                var failureText = string.Join(
                    Environment.NewLine,
                    result.Failures.Take(8).Select(failure =>
                        $"{Path.GetFileName(failure.Path)}：{failure.Reason}"));
                System.Windows.MessageBox.Show(
                    this,
                    $"部分项目未能导入：\n\n{failureText}",
                    "导入结果",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception exception)
        {
            ShowStatus("导入失败，原项目未被删除");
            System.Windows.MessageBox.Show(
                this,
                $"导入未能保存，已尝试回滚本次操作。\n\n{exception.Message}",
                "导入失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isImporting = false;
        }
    }

    private void UpdateDragState(DragEventArgs e)
    {
        var acceptsDrop = !_isImporting && TryGetDroppedPaths(e.Data, out _);
        e.Effects = acceptsDrop ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Visibility = acceptsDrop ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private static bool TryGetDroppedPaths(IDataObject data, out string[] paths)
    {
        paths = [];
        if (!data.GetDataPresent(DataFormats.FileDrop))
        {
            return false;
        }

        paths = data.GetData(DataFormats.FileDrop) as string[] ?? [];
        return paths.Length > 0;
    }

    private void ShowStatus(string message)
    {
        ImportStatusText.Text = message;
        ImportStatus.Visibility = Visibility.Visible;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private void OnStatusTimerTick(object? sender, EventArgs e)
    {
        _statusTimer.Stop();
        ImportStatus.Visibility = Visibility.Collapsed;
    }

    private async void OnIconTileClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not WpfButton { DataContext: IconTileViewModel tile })
        {
            return;
        }

        if (tile.IsMoreButton)
        {
            ExpandedRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (tile.ItemId is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (string.Equals(_lastLaunchedItemId, tile.ItemId, StringComparison.Ordinal)
            && now - _lastLaunchClickAt < TimeSpan.FromMilliseconds(700))
        {
            return;
        }

        _lastLaunchedItemId = tile.ItemId;
        _lastLaunchClickAt = now;

        try
        {
            await _viewModel.LaunchItemAsync(tile.ItemId);
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                this,
                $"无法打开“{tile.Label}”。请确认目标仍然存在且当前用户有权访问。\n\n" +
                exception.Message,
                "打开失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnCloseButtonClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        RequestClose();
    }

    private void OnCloseMenuItemClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        RequestClose();
    }

    private void OnSettingsMenuItemClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnHideMenuItemClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        HideRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RequestClose()
    {
        if (CloseRequested is not null)
        {
            CloseRequested.Invoke(this, EventArgs.Empty);
            return;
        }

        Close();
    }

    private static T? FindVisualAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
            {
                return match;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }
}
