using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace ArmControlSuite.Views.Dialogs;

public partial class ConfirmDialogWindow : FluentWindow
{
    public ConfirmDialogWindow(string title, string message, string confirmText, bool isDanger)
    {
        InitializeComponent();
        Title = title;
        DialogTitleBar.Title = title;
        TitleTextBlock.Text = title;
        MessageTextBlock.Text = message;
        ConfirmButton.Content = confirmText;

        if (isDanger)
        {
            DialogIcon.Symbol = SymbolRegular.Warning24;
            DialogIcon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
            ConfirmButton.Appearance = ControlAppearance.Danger;
            ConfirmButton.Icon = new SymbolIcon(SymbolRegular.Delete20);
        }
        else
        {
            DialogIcon.Symbol = SymbolRegular.ArrowReset24;
            DialogIcon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));
            ConfirmButton.Appearance = ControlAppearance.Primary;
            ConfirmButton.Icon = new SymbolIcon(SymbolRegular.Checkmark20);
        }
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
