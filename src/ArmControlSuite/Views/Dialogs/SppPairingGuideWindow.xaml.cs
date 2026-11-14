using System.Windows;
using Wpf.Ui.Controls;

namespace ArmControlSuite.Views.Dialogs;

public partial class SppPairingGuideWindow : FluentWindow
{
    public SppPairingGuideWindow()
    {
        InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
