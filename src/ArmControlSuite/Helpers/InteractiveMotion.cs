using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace ArmControlSuite.Helpers;

/// <summary>
/// 统一管理按钮微交互弹性缩放、Tab 方向滑入淡入、Toast 入场及连接呼吸灯动画。
/// </summary>
public static class InteractiveMotion
{
    private static int _globalHandlersRegistered;
    private static readonly Duration HoverDuration = TimeSpan.FromMilliseconds(140);
    private static readonly Duration PressDuration = TimeSpan.FromMilliseconds(75);
    private static readonly Duration ReboundDuration = TimeSpan.FromMilliseconds(220);

    public static void RegisterGlobalButtonMotion()
    {
        if (Interlocked.Exchange(ref _globalHandlersRegistered, 1) != 0)
            return;

        EventManager.RegisterClassHandler(
            typeof(Button),
            UIElement.MouseEnterEvent,
            new MouseEventHandler(OnButtonMouseEnter),
            handledEventsToo: true);

        EventManager.RegisterClassHandler(
            typeof(Button),
            UIElement.MouseLeaveEvent,
            new MouseEventHandler(OnButtonMouseLeave),
            handledEventsToo: true);

        EventManager.RegisterClassHandler(
            typeof(Button),
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(OnButtonPreviewMouseDown),
            handledEventsToo: true);

        EventManager.RegisterClassHandler(
            typeof(Button),
            UIElement.PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(OnButtonPreviewMouseUp),
            handledEventsToo: true);
    }

    private static void OnButtonMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Button btn && btn.IsEnabled && btn.ActualWidth > 0)
        {
            double target = btn.ActualWidth <= 44 ? 1.04 : 1.018;
            AnimateButtonScale(btn, target, HoverDuration, useBounce: false);
        }
    }

    private static void OnButtonMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Button btn)
        {
            AnimateButtonScale(btn, 1.0, HoverDuration, useBounce: false);
        }
    }

    private static void OnButtonPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button btn && btn.IsEnabled && e.ChangedButton == MouseButton.Left)
        {
            AnimateButtonScale(btn, 0.945, PressDuration, useBounce: false);
        }
    }

    private static void OnButtonPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button btn && e.ChangedButton == MouseButton.Left)
        {
            double rest = btn.IsMouseOver && btn.IsEnabled
                ? (btn.ActualWidth <= 44 ? 1.04 : 1.018)
                : 1.0;
            AnimateButtonScale(btn, rest, ReboundDuration, useBounce: true);
        }
    }

    private static void AnimateButtonScale(Button button, double targetScale, Duration duration, bool useBounce)
    {
        ScaleTransform scale = EnsureScaleTransform(button);
        IEasingFunction easing = useBounce
            ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 }
            : new CubicEase { EasingMode = EasingMode.EaseOut };

        var anim = new DoubleAnimation(targetScale, duration)
        {
            EasingFunction = easing
        };

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim, HandoffBehavior.SnapshotAndReplace);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim, HandoffBehavior.SnapshotAndReplace);
    }

    public static void AnimateTabEntrance(FrameworkElement target, int direction)
    {
        TranslateTransform translate = EnsureTranslateTransform(target);
        double offset = direction == 0 ? 0 : (direction > 0 ? 22 : -22);

        var slide = new DoubleAnimation(offset, 0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var fade = new DoubleAnimation(0.28, 1.0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        translate.BeginAnimation(TranslateTransform.XProperty, slide, HandoffBehavior.SnapshotAndReplace);
        target.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
    }

    public static void AnimateToastEntrance(FrameworkElement toast)
    {
        TranslateTransform translate = EnsureTranslateTransform(toast);

        var drop = new DoubleAnimation(-16, 0, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var fade = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(190))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        translate.BeginAnimation(TranslateTransform.YProperty, drop, HandoffBehavior.SnapshotAndReplace);
        toast.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
    }

    public static void AnimateExpandFade(FrameworkElement element)
    {
        TranslateTransform translate = EnsureTranslateTransform(element);

        var slide = new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(190))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var fade = new DoubleAnimation(0.15, 1.0, TimeSpan.FromMilliseconds(190))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        translate.BeginAnimation(TranslateTransform.YProperty, slide, HandoffBehavior.SnapshotAndReplace);
        element.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
    }

    public static void SetConnectedDotPulse(Ellipse dot, bool isConnected)
    {
        ScaleTransform scale = EnsureScaleTransform(dot);
        if (isConnected)
        {
            var opacityPulse = new DoubleAnimation(1.0, 0.35, TimeSpan.FromMilliseconds(950))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            var scalePulse = new DoubleAnimation(1.0, 1.24, TimeSpan.FromMilliseconds(950))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            dot.BeginAnimation(UIElement.OpacityProperty, opacityPulse);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, scalePulse);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scalePulse);
        }
        else
        {
            dot.BeginAnimation(UIElement.OpacityProperty, null);
            dot.Opacity = 1.0;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = 1.0;
            scale.ScaleY = 1.0;
        }
    }

    private static ScaleTransform EnsureScaleTransform(UIElement element)
    {
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        if (element.RenderTransform is ScaleTransform existing)
            return existing;

        if (element.RenderTransform is TransformGroup group)
        {
            foreach (var child in group.Children)
            {
                if (child is ScaleTransform found)
                    return found;
            }
            var added = new ScaleTransform(1.0, 1.0);
            group.Children.Add(added);
            return added;
        }

        var created = new ScaleTransform(1.0, 1.0);
        if (element.RenderTransform != null && element.RenderTransform != Transform.Identity)
        {
            var tg = new TransformGroup();
            tg.Children.Add(element.RenderTransform);
            tg.Children.Add(created);
            element.RenderTransform = tg;
        }
        else
        {
            element.RenderTransform = created;
        }
        return created;
    }

    private static TranslateTransform EnsureTranslateTransform(UIElement element)
    {
        if (element.RenderTransform is TranslateTransform existing)
            return existing;

        if (element.RenderTransform is TransformGroup group)
        {
            foreach (var child in group.Children)
            {
                if (child is TranslateTransform found)
                    return found;
            }
            var added = new TranslateTransform(0, 0);
            group.Children.Add(added);
            return added;
        }

        var created = new TranslateTransform(0, 0);
        if (element.RenderTransform != null && element.RenderTransform != Transform.Identity)
        {
            var tg = new TransformGroup();
            tg.Children.Add(element.RenderTransform);
            tg.Children.Add(created);
            element.RenderTransform = tg;
        }
        else
        {
            element.RenderTransform = created;
        }
        return created;
    }
}
