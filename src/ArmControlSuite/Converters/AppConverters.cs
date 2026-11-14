using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace ArmControlSuite.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool flag = value is bool b && b;
        if (Invert || string.Equals(parameter?.ToString(), "Invert", StringComparison.OrdinalIgnoreCase))
            flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool visible = value is Visibility v && v == Visibility.Visible;
        if (Invert || string.Equals(parameter?.ToString(), "Invert", StringComparison.OrdinalIgnoreCase))
            visible = !visible;
        return visible;
    }
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : false;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}

public sealed class IntEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return false;
        return int.TryParse(parameter.ToString(), out int target)
               && int.TryParse(value.ToString(), out int current)
               && current == target;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isChecked && isChecked && parameter != null && int.TryParse(parameter.ToString(), out int target))
            return target;
        return Binding.DoNothing;
    }
}

public sealed class HexToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush FallbackBrush = new(Colors.Gray);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                return new SolidColorBrush(color);
            }
            catch
            {
                return FallbackBrush;
            }
        }
        return FallbackBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class BoolToOpacityConverter : IValueConverter
{
    public double TrueOpacity { get; set; } = 1.0;
    public double FalseOpacity { get; set; } = 0.50;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? TrueOpacity : FalseOpacity;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class PlaybackStatusToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        int status = value is int i ? i : 0;
        return status switch
        {
            1 => SymbolRegular.Play20,
            2 => SymbolRegular.Pause20,
            _ => SymbolRegular.Stop20
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class PlaybackStatusToBrushConverter : IValueConverter
{
    private static readonly Brush PlayingBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669"));
    private static readonly Brush PausedBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706"));
    private static readonly Brush StoppedBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        int status = value is int i ? i : 0;
        return status switch
        {
            1 => PlayingBrush,
            2 => PausedBrush,
            _ => StoppedBrush
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
