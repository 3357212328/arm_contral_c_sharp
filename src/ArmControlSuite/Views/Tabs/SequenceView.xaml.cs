using System.Windows.Controls;
using System.Windows.Input;

namespace ArmControlSuite.Views.Tabs;

public partial class SequenceView : UserControl
{
    public SequenceView()
    {
        InitializeComponent();
    }

    private void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(char.IsDigit);
    }
}
