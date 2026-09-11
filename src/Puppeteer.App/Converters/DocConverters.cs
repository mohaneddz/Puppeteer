using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Puppeteer.App.Converters;

/// <summary>Colours a doc's lifecycle label. Undocumented and unlabelled projects stay grey rather
/// than borrowing an alarming colour — not having written the doc yet is not a problem with the code.</summary>
public sealed class DocStatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Brush((value as string) switch
    {
        "Active" => "SuccessBrush",
        "Paused" => "InfoBrush",
        "Shipped" or "Idea" => "AccentBrush",
        "Abandoned" => "DangerBrush",
        "Archived" or "Unlabelled" => "MutedTextBrush",
        _ => "FaintBrush",
    });

    private static Brush Brush(string key) => Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>A shape per doc lifecycle state, so the list reads at a glance instead of relying on a
/// colour key. Archived and Abandoned reuse the same glyphs the project-lifecycle status filter uses
/// for the same ideas ("boxed away", "crossed out") — this is the doc equivalent, not the same field.</summary>
public sealed class DocStatusIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Application.Current.Resources[(value as string) switch
    {
        "Active" => "IconActivity",
        "Paused" => "IconPause",
        "Shipped" => "IconFlag",
        "Idea" => "IconLightbulb",
        "Abandoned" => "IconStatusCancelled",
        "Archived" => "IconArchive",
        "Unlabelled" => "IconDoc",
        _ => "IconStatusUnset",
    }];
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Formats a captured snapshot as one scannable line for the history list.</summary>
public sealed class SnapshotLineConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Core.ProjectStateSnapshot snapshot) return "";
        var parts = new List<string>();
        if (snapshot.Branch is { Length: > 0 } branch) parts.Add(branch);
        if (snapshot.Head is { Length: > 0 } head) parts.Add(head);
        if (snapshot.ModifiedFileCount > 0) parts.Add($"{snapshot.ModifiedFileCount} dirty");
        if (snapshot.Ahead > 0) parts.Add($"↑{snapshot.Ahead}");
        if (snapshot.Behind > 0) parts.Add($"↓{snapshot.Behind}");
        if (parts.Count == 0) parts.Add("clean");
        return string.Join(" · ", parts);
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class LocalDateConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTimeOffset when ? when.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "";
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>True when two bound values read the same. Used to light the reader's active tab, where
/// the tab and the selection live on different data contexts.</summary>
public sealed class EqualsMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length == 2 && string.Equals(values[0]?.ToString(), values[1]?.ToString(), StringComparison.Ordinal);

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
