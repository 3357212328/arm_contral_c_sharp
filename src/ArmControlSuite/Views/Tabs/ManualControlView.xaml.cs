using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ArmControlSuite.ViewModels;

namespace ArmControlSuite.Views.Tabs;

public partial class ManualControlView : UserControl
{
    private bool _isDraggingSlider;

    public ManualControlView()
    {
        InitializeComponent();
    }

    private void ServoSlider_DragStarted(object sender, DragStartedEventArgs e)
    {
        _isDraggingSlider = true;
    }

    private void ServoSlider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _isDraggingSlider = false;
        if (sender is FrameworkElement { DataContext: ServoChannelViewModel vm })
        {
            vm.CommitSliderAngle();
        }
    }

    private void ServoSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 单击轨道直接跳转角度后立即提交；若刚结束拖动则由 DragCompleted 处理
        if (!_isDraggingSlider && sender is FrameworkElement { DataContext: ServoChannelViewModel vm })
        {
            Dispatcher.BeginInvoke(() => vm.CommitSliderAngle());
        }
    }

    private void ServoSlider_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
        {
            if (sender is FrameworkElement { DataContext: ServoChannelViewModel vm })
                vm.CommitSliderAngle();
        }
    }

    private void AngleTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(char.IsDigit);
    }

    private void AngleTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is FrameworkElement { DataContext: ServoChannelViewModel vm })
        {
            vm.CommitTextAngle();
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void AngleTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ServoChannelViewModel vm })
        {
            vm.CommitTextAngle();
        }
    }
}
