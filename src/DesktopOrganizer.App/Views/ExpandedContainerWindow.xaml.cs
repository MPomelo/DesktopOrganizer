using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using DesktopOrganizer.App.Native;
using DesktopOrganizer.App.ViewModels;
using WpfButton = System.Windows.Controls.Button;

namespace DesktopOrganizer.App.Views;

public partial class ExpandedContainerWindow : Window
{
    private readonly ContainerViewModel _viewModel;
    private string? _lastLaunchedItemId;
    private DateTimeOffset _lastLaunchClickAt;
    private bool _isShowingDialog;
    private bool _isClosing;

    public ExpandedContainerWindow(ContainerViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        SourceInitialized += OnSourceInitialized;
        Closed += OnWindowClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        WindowStyleService.HideFromAltTab(handle);
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _isClosing = true;
        SourceInitialized -= OnSourceInitialized;
        Closed -= OnWindowClosed;
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not WpfButton)
        {
            DragMove();
        }
    }

    private void OnWindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;
        Close();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        if (_isShowingDialog || _isClosing)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (!IsActive && !_isShowingDialog && !_isClosing)
            {
                Close();
            }
        }, DispatcherPriority.ApplicationIdle);
    }

    private async void OnIconTileClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not WpfButton { DataContext: IconTileViewModel { ItemId: not null } tile })
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
            _isShowingDialog = true;
            try
            {
                System.Windows.MessageBox.Show(
                    this,
                    $"无法打开“{tile.Label}”。请确认目标仍然存在且当前用户有权访问。\n\n" +
                    exception.Message,
                    "打开失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                _isShowingDialog = false;
            }
        }
    }

    private void OnCloseButtonClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        Close();
    }
}
