using System.Globalization;
using System.Windows;
using System.Windows.Data;
using V2RayLite.Core;
using MediaColor = System.Windows.Media.Color;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace V2RayLite.App;

public sealed class PageVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class BoolVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class InverseBoolVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class NodeStatusBrushConverter : IValueConverter
{
    public static NodeStatusBrushConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            NodeStatus.Available => new MediaSolidColorBrush(MediaColor.FromRgb(31, 191, 90)),
            NodeStatus.Testing => new MediaSolidColorBrush(MediaColor.FromRgb(11, 119, 255)),
            NodeStatus.Unavailable => new MediaSolidColorBrush(MediaColor.FromRgb(145, 155, 168)),
            _ => new MediaSolidColorBrush(MediaColor.FromRgb(31, 191, 90))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class NodeDelayBrushConverter : IValueConverter
{
    public static NodeDelayBrushConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            NodeStatus.Available => new MediaSolidColorBrush(MediaColor.FromRgb(24, 174, 77)),
            NodeStatus.Testing => new MediaSolidColorBrush(MediaColor.FromRgb(11, 119, 255)),
            NodeStatus.Unavailable => new MediaSolidColorBrush(MediaColor.FromRgb(145, 155, 168)),
            _ => new MediaSolidColorBrush(MediaColor.FromRgb(255, 126, 26))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class FlagTextConverter : IValueConverter
{
    public static FlagTextConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var name = value?.ToString() ?? string.Empty;
        if (name.Contains("香港", StringComparison.OrdinalIgnoreCase) || name.Contains("hk", StringComparison.OrdinalIgnoreCase)) return "港";
        if (name.Contains("日本", StringComparison.OrdinalIgnoreCase) || name.Contains("jp", StringComparison.OrdinalIgnoreCase)) return "日";
        if (name.Contains("新加坡", StringComparison.OrdinalIgnoreCase) || name.Contains("sg", StringComparison.OrdinalIgnoreCase)) return "新";
        if (name.Contains("美国", StringComparison.OrdinalIgnoreCase) || name.Contains("us", StringComparison.OrdinalIgnoreCase)) return "美";
        if (name.Contains("德国", StringComparison.OrdinalIgnoreCase) || name.Contains("de", StringComparison.OrdinalIgnoreCase)) return "德";
        return "节";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class NodeActionTextConverter : IValueConverter
{
    public static NodeActionTextConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? "使用中" : "连接";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
