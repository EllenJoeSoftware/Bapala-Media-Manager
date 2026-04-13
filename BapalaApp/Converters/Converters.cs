using System.Globalization;

namespace BapalaApp.Converters;

/// <summary>Returns true when the bound string is non-null and non-empty.</summary>
public class StringNotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && !string.IsNullOrWhiteSpace(s);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Returns true when the bound value is not null (used for Rating visibility).</summary>
public class NotNullConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Returns true when the bound int is greater than zero (pagination bar).</summary>
public class GreaterThanZeroConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int n && n > 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Maps a bool (filter chip active?) to the appropriate Button style.
/// true  → ActiveChipButton (red)
/// false → ChipButton       (grey)
/// </summary>
public class BoolToChipStyleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var active = value is bool b && b;
        var key    = active ? "ActiveChipButton" : "ChipButton";
        return Application.Current!.Resources.TryGetValue(key, out var style)
            ? style
            : new Style(typeof(Button));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
