using System.Windows.Controls;
using System.Windows.Input;

namespace ArmControlSuite.Views.Tabs;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(char.IsDigit);
    }
}
