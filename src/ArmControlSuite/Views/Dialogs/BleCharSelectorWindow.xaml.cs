using System.Windows;
using ArmControlSuite.ViewModels;
using Wpf.Ui.Controls;

namespace ArmControlSuite.Views.Dialogs;

public partial class BleCharSelectorWindow : FluentWindow
{
    public BleCharSelectorViewModel ViewModel { get; }

    public BleCharSelectorWindow(BleCharSelectorViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.ValidateSelection())
            return;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
