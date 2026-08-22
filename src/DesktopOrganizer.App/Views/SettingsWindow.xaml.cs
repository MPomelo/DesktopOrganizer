using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopOrganizer.App.ViewModels;
using DragDrop = System.Windows.DragDrop;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace DesktopOrganizer.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private Point _dragStart;
    private SettingsIconItemViewModel? _draggedItem;
    private bool _isCommitted;
    private bool _isSaving;

    public bool FullExitRequested { get; private set; }

    public SettingsWindow(SettingsViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void OnIconListPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(IconList);
        _draggedItem = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext
            as SettingsIconItemViewModel;
    }

    private void OnIconListPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedItem is null)
        {
            return;
        }

        var current = e.GetPosition(IconList);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(IconList, _draggedItem, DragDropEffects.Move);
        _draggedItem = null;
    }

    private void OnIconListDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(SettingsIconItemViewModel))
                is not SettingsIconItemViewModel source
            || FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext
                is not SettingsIconItemViewModel target)
        {
            return;
        }

        _viewModel.MoveItem(source, target);
        IconList.SelectedItem = source;
        e.Handled = true;
    }

    private void OnRestoreSystemThemeClick(object sender, RoutedEventArgs e)
    {
        _viewModel.RestoreSystemTheme();
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_isSaving || !_viewModel.Validate())
        {
            return;
        }

        _isSaving = true;
        SaveButton.IsEnabled = false;
        try
        {
            await _viewModel.SaveAsync();
            _isCommitted = true;
            DialogResult = true;
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                this,
                $"设置未能保存，预览修改已回滚。\n\n{exception.Message}",
                "保存设置失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            _isCommitted = true;
            DialogResult = false;
        }
        finally
        {
            _isSaving = false;
            SaveButton.IsEnabled = true;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnFullExitClick(object sender, RoutedEventArgs e)
    {
        _viewModel.Cancel();
        _isCommitted = true;
        FullExitRequested = true;
        Close();
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (!_isCommitted)
        {
            _viewModel.Cancel();
        }
    }

    private static T? FindAncestor<T>(DependencyObject? source)
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
