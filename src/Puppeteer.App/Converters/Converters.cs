using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Puppeteer.Core;

namespace Puppeteer.App.Converters;

public sealed class StringEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) => Binding.DoNothing;
}

public sealed class StringEqualsBoolConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) =>
        value is true ? parameter?.ToString() ?? Binding.DoNothing : Binding.DoNothing;
}

public sealed class NullVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c) =>
        (value is null) ^ string.Equals(parameter?.ToString(), "Inverse") ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) => Binding.DoNothing;
}

public sealed class BoolVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c)
    {
        var b = value is true;
        if (string.Equals(parameter?.ToString(), "Inverse")) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) => Binding.DoNothing;
}

public sealed class EmptyStringVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c)
    {
        var empty = string.IsNullOrWhiteSpace(value?.ToString());
        if (string.Equals(parameter?.ToString(), "Inverse")) return empty ? Visibility.Visible : Visibility.Collapsed;
        return empty ? Visibility.Collapsed : Visibility.Visible;
    }
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) => Binding.DoNothing;
}

public sealed class CountVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c)
    {
        var count = value is int i ? i : (value is System.Collections.ICollection col ? col.Count : 0);
        var invert = string.Equals(parameter?.ToString(), "Inverse");
        return (count > 0) ^ invert ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Green when a project has a running session, amber when it has git changes, otherwise hidden.</summary>
public sealed class ProjectStatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c)
    {
        if (value is Project p)
        {
            if (p.IsRunning) return TryBrush("SuccessBrush");
            if (p.HasGitChanges) return TryBrush("AccentBrush");
        }
        return Brushes.Transparent;
    }
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) => Binding.DoNothing;
    internal static Brush TryBrush(string key) => (Brush)Application.Current.Resources[key];
}

public sealed class ProjectStatusVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c) =>
        value is Project p && (p.IsRunning || p.HasGitChanges) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Amber when the current view mode matches the parameter, muted otherwise.</summary>
public sealed class ViewStrokeConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c)
    {
        var active = string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);
        return (Brush)Application.Current.Resources[active ? "AccentBrush" : "MutedTextBrush"];
    }
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Lifts the status toast above the terminal panel when it is open.</summary>
public sealed class ToastMarginConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c) =>
        new Thickness(0, 0, 0, value is true ? 268 : 26);
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Grid mode → fixed card width; List mode → auto (stretches to the row).</summary>
public sealed class ViewModeWidthConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c) =>
        string.Equals(value?.ToString(), "List", StringComparison.OrdinalIgnoreCase) ? double.NaN : 316d;
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Grid mode → fixed card height (so the virtualizing wrap layout can assume a uniform
/// cell); List mode → auto.</summary>
public sealed class ViewModeHeightConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c) =>
        string.Equals(value?.ToString(), "List", StringComparison.OrdinalIgnoreCase) ? double.NaN : 178d;
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Given a total count and a shown-count parameter N, yields "+K" for the hidden remainder
/// (empty when nothing is hidden).</summary>
public sealed class MoreCountConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c)
    {
        var total = value is int i ? i : 0;
        var shown = int.TryParse(parameter?.ToString(), out var n) ? n : 4;
        return total > shown ? $"+{total - shown}" : "";
    }
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Trims a list to its first N items (N from the parameter) so a card's tech badges stay on
/// one row and the fixed-height grid cell never clips. The full stack still shows in the inspector.</summary>
public sealed class TakeConverter : IValueConverter
{
    public object? Convert(object? value, Type t, object? parameter, CultureInfo c)
    {
        if (value is not System.Collections.IEnumerable items) return value;
        var count = int.TryParse(parameter?.ToString(), out var n) ? n : 4;
        return items.Cast<object>().Take(count).ToList();
    }
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Shared set of pinned project ids. Held statically so a per-card converter can read it;
/// the grid rebuilds (and thus re-evaluates the binding) whenever pins change.</summary>
public static class PinStore
{
    public static readonly HashSet<Guid> Ids = [];
}

public sealed class PinnedVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c)
    {
        var pinned = value is Guid id && PinStore.Ids.Contains(id);
        if (string.Equals(parameter?.ToString(), "Inverse")) pinned = !pinned;
        return pinned ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Highlights the folder-tree row whose path matches the selected folder.</summary>
public sealed class ActiveFolderBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type t, object? parameter, CultureInfo c)
    {
        if (values is [string nodePath, Puppeteer.App.ViewModels.FolderNode selected] && string.Equals(nodePath, selected.Path, StringComparison.OrdinalIgnoreCase))
            return (Brush)Application.Current.Resources["AccentSoftBrush"];
        return Brushes.Transparent;
    }
    public object[] ConvertBack(object? value, Type[] t, object? parameter, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Yields a project's product type ("Desktop", "Website"…) from its detected stack.</summary>
public sealed class ProjectTypeConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c) =>
        value is Project p ? ProjectTypeRules.Of(p) : "";
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) => Binding.DoNothing;
}

public sealed class TechIconBrushConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c) =>
        new SolidColorBrush(Tech.Color(value?.ToString()));
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) => Binding.DoNothing;
}

public sealed class TechIconTintConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c)
    {
        var col = Tech.Color(value?.ToString());
        return new SolidColorBrush(Color.FromArgb(38, col.R, col.G, col.B));
    }
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) => Binding.DoNothing;
}

public sealed class TechMonogramConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c) => Tech.Monogram(value?.ToString());
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) => Binding.DoNothing;
}

internal static class Tech
{
    public static Color Color(string? tech) => tech switch
    {
        "Tauri" => FromHex("#24C8DB"),
        "Flutter" => FromHex("#54C5F8"),
        "Next.js" => FromHex("#E8ECF1"),
        "Qt" => FromHex("#41CD52"),
        "Vue" => FromHex("#42B883"),
        "Rust" => FromHex("#E86A4B"),
        "Python" => FromHex("#4B8BBE"),
        "Android" => FromHex("#3DDC84"),
        "Godot" => FromHex("#478CBF"),
        "Node.js" => FromHex("#68A063"),
        "Vite" => FromHex("#B65CFF"),
        _ => FromHex("#8B5CF6"), // .NET / default purple
    };

    public static string Monogram(string? tech) => tech switch
    {
        "Tauri" => "T",
        "Flutter" => "F",
        "Next.js" => "N",
        "Qt" => "Qt",
        "Vue" => "V",
        "Rust" => "R",
        "Python" => "Py",
        "Android" => "A",
        "Godot" => "G",
        "Node.js" => "N",
        "Vite" => "V",
        _ => ".N",
    };

    private static Color FromHex(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
