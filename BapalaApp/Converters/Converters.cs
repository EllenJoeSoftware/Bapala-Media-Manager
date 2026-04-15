using System.Globalization;

namespace BapalaApp.Converters;

/// <summary>Returns true when an int is greater than zero. Used to show/hide the discovered server list.</summary>
public class IntToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int n && n > 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Inverts a boolean value — used to hide the rescan button while scanning is active.</summary>
public class InvertedBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

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
///
/// NOTE: Binding the Style property itself is unreliable with compiled bindings.
/// Prefer BoolToChipBgConverter + BoolToChipFgConverter on individual properties.
/// This converter is kept for any non-compiled-binding scenarios.
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

/// <summary>
/// Maps a bool to a BackgroundColor for filter chips.
/// true  → AccentColor (#e50914 red)
/// false → Surface2Color (#242424 dark grey)
/// </summary>
public class BoolToChipBgConverter : IValueConverter
{
    // Active colour matches AccentColor in Colors.xaml
    private static readonly Color ActiveBg  = Color.FromArgb("#e50914");
    private static readonly Color InactiveBg = Color.FromArgb("#242424");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? ActiveBg : InactiveBg;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Maps a bool to a TextColor for filter chips.
/// true  → White  (on the red active chip)
/// false → #aaaaaa (Text2Color, on the grey inactive chip)
/// </summary>
public class BoolToChipFgConverter : IValueConverter
{
    private static readonly Color ActiveFg   = Colors.White;
    private static readonly Color InactiveFg = Color.FromArgb("#aaaaaa");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? ActiveFg : InactiveFg;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
