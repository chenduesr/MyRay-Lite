using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using V2RayLite.Core;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfPoint = System.Windows.Point;

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
            NodeStatus.Available => Solid("#1FBF5A"),
            NodeStatus.Testing => Solid("#0B77FF"),
            NodeStatus.Unavailable => Solid("#919BA8"),
            _ => Solid("#1FBF5A")
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static SolidColorBrush Solid(string color) => new((WpfColor)WpfColorConverter.ConvertFromString(color));
}

public sealed class NodeDelayBrushConverter : IValueConverter
{
    public static NodeDelayBrushConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            NodeStatus.Available => Solid("#18AE4D"),
            NodeStatus.Testing => Solid("#0B77FF"),
            NodeStatus.Unavailable => Solid("#919BA8"),
            _ => Solid("#FF7E1A")
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static SolidColorBrush Solid(string color) => new((WpfColor)WpfColorConverter.ConvertFromString(color));
}

public sealed class NodeStatusTextConverter : IValueConverter
{
    public static NodeStatusTextConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            NodeStatus.Available => "可用",
            NodeStatus.Testing => "测速中",
            NodeStatus.Unavailable => "不可用",
            _ => "未测速"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class NodeStatusSoftBrushConverter : IValueConverter
{
    public static NodeStatusSoftBrushConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            NodeStatus.Available => Solid("#E9FBEF"),
            NodeStatus.Testing => Solid("#EAF3FF"),
            NodeStatus.Unavailable => Solid("#FFF4E8"),
            _ => Solid("#F1F5F9")
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static SolidColorBrush Solid(string color)
    {
        var brush = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
public sealed class FlagImageConverter : IValueConverter
{
    public static FlagImageConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return BuildFlag(DetectRegion(value?.ToString() ?? string.Empty));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static string DetectRegion(string name)
    {
        if (ContainsAny(name, "香港", "hk", "hong")) return "hk";
        if (ContainsAny(name, "日本", "jp", "japan")) return "jp";
        if (ContainsAny(name, "新加坡", "sg", "singapore")) return "sg";
        if (ContainsAny(name, "美国", "us", "usa", "la", "america")) return "us";
        if (ContainsAny(name, "德国", "de", "germany")) return "de";
        if (ContainsAny(name, "韩国", "kr", "korea")) return "kr";
        if (ContainsAny(name, "台湾", "tw", "taiwan")) return "tw";
        if (ContainsAny(name, "英国", "uk", "gb", "england")) return "uk";
        if (ContainsAny(name, "加拿大", "ca", "canada")) return "ca";
        if (ContainsAny(name, "印度", "in", "india")) return "in";
        return "default";
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
        return tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static ImageSource BuildFlag(string region)
    {
        var group = new DrawingGroup();
        DrawCircle(group, Solid("#F0F5FB"), 16, 16, 16);

        switch (region)
        {
            case "hk":
                DrawRect(group, "#E11D2E", 0, 0, 32, 32);
                DrawCircle(group, WpfBrushes.White, 16, 16, 5.5);
                DrawCircle(group, Solid("#E11D2E"), 16, 16, 2.3);
                break;
            case "jp":
                DrawRect(group, "#FFFFFF", 0, 0, 32, 32);
                DrawCircle(group, Solid("#D91F3C"), 16, 16, 7);
                break;
            case "sg":
                DrawRect(group, "#EF3340", 0, 0, 32, 16);
                DrawRect(group, "#FFFFFF", 0, 16, 32, 16);
                DrawCircle(group, WpfBrushes.White, 10, 8, 4.6);
                DrawCircle(group, Solid("#EF3340"), 12, 8, 4);
                break;
            case "us":
                DrawRect(group, "#FFFFFF", 0, 0, 32, 32);
                for (var i = 0; i < 7; i++)
                {
                    DrawRect(group, "#B22234", 0, i * 4.8, 32, 2.4);
                }
                DrawRect(group, "#3A3F8F", 0, 0, 14, 15);
                break;
            case "de":
                DrawRect(group, "#111111", 0, 0, 32, 10.7);
                DrawRect(group, "#DD0000", 0, 10.7, 32, 10.7);
                DrawRect(group, "#FFCE00", 0, 21.4, 32, 10.6);
                break;
            case "kr":
                DrawRect(group, "#FFFFFF", 0, 0, 32, 32);
                DrawCircle(group, Solid("#CD2E3A"), 16, 14, 6);
                DrawCircle(group, Solid("#0047A0"), 16, 18, 6);
                break;
            case "tw":
                DrawRect(group, "#FE0000", 0, 0, 32, 32);
                DrawRect(group, "#000095", 0, 0, 16, 16);
                DrawCircle(group, WpfBrushes.White, 8, 8, 4);
                break;
            case "uk":
                DrawRect(group, "#012169", 0, 0, 32, 32);
                DrawRect(group, "#FFFFFF", 13, 0, 6, 32);
                DrawRect(group, "#FFFFFF", 0, 13, 32, 6);
                DrawRect(group, "#C8102E", 14.5, 0, 3, 32);
                DrawRect(group, "#C8102E", 0, 14.5, 32, 3);
                break;
            case "ca":
                DrawRect(group, "#D52B1E", 0, 0, 8, 32);
                DrawRect(group, "#FFFFFF", 8, 0, 16, 32);
                DrawRect(group, "#D52B1E", 24, 0, 8, 32);
                DrawCircle(group, Solid("#D52B1E"), 16, 16, 4);
                break;
            case "in":
                DrawRect(group, "#FF9933", 0, 0, 32, 10.7);
                DrawRect(group, "#FFFFFF", 0, 10.7, 32, 10.7);
                DrawRect(group, "#138808", 0, 21.4, 32, 10.6);
                DrawCircle(group, Solid("#000080"), 16, 16, 3);
                break;
            default:
                DrawRect(group, "#EAF3FF", 0, 0, 32, 32);
                DrawCircle(group, Solid("#0875F8"), 16, 16, 7);
                break;
        }

        group.ClipGeometry = new EllipseGeometry(new WpfPoint(16, 16), 16, 16);
        group.Freeze();
        return new DrawingImage(group);
    }

    private static void DrawRect(DrawingGroup group, string color, double x, double y, double width, double height)
    {
        group.Children.Add(new GeometryDrawing(Solid(color), null, new RectangleGeometry(new Rect(x, y, width, height))));
    }

    private static void DrawCircle(DrawingGroup group, WpfBrush brush, double x, double y, double radius)
    {
        group.Children.Add(new GeometryDrawing(brush, null, new EllipseGeometry(new WpfPoint(x, y), radius, radius)));
    }

    private static SolidColorBrush Solid(string color)
    {
        var brush = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
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
