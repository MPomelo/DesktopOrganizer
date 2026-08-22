using System.Windows;
using DesktopOrganizer.App.Models;
using DesktopOrganizer.App.ViewModels;

namespace DesktopOrganizer.App.Views;

public partial class ReleaseConfirmationWindow : Window
{
    private readonly ReleaseConfirmationViewModel _viewModel;

    public ReleaseConfirmationWindow(ContainerReleasePlan plan)
    {
        _viewModel = new ReleaseConfirmationViewModel(plan);
        DataContext = _viewModel;
        InitializeComponent();
        Loaded += (_, _) => CancelButton.Focus();
    }

    public int SelectedDesktopCount => _viewModel.DesktopCount;

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
