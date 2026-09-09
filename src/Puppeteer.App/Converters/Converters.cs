using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Media = System.Windows.Media;
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

/// <summary>Picks a chevron pointing the direction a collapse toggle button will move its panel:
/// for a "Left" side panel (sidebar), collapsed points right (reveal), expanded points left (hide);
/// for a "Right" side panel (details), it's the mirror.</summary>
public sealed class PanelToggleIconConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c)
    {
        var collapsed = value is true;
        var leftSide = string.Equals(parameter?.ToString(), "Left", StringComparison.OrdinalIgnoreCase);
        var pointsRight = leftSide ? collapsed : !collapsed;
        return Application.Current.Resources[pointsRight ? "IconChevronRight" : "IconChevronLeft"];
    }
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
    public object Convert(object? value, Type t, object? parameter, CultureInfo c) => Tech.Brush(value?.ToString());
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) => Binding.DoNothing;
}

public sealed class TechIconTintConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c) => Tech.TintBrush(value?.ToString());
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) => Binding.DoNothing;
}

public sealed class TechMonogramConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c) => Tech.Monogram(value?.ToString());
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) => Binding.DoNothing;
}

internal static class Tech
{
    // Brushes are cached and frozen: these converters run for every card the grid realizes, and an
    // unfrozen SolidColorBrush per call means a fresh allocation and a change listener each time.
    private static readonly Dictionary<string, Brush> Solid = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Brush> Tint = new(StringComparer.OrdinalIgnoreCase);

    public static Brush Brush(string? tech) => Cached(Solid, tech, c => new SolidColorBrush(c));

    public static Brush TintBrush(string? tech) => Cached(Tint, tech, c => new SolidColorBrush(Media.Color.FromArgb(38, c.R, c.G, c.B)));

    private static Brush Cached(Dictionary<string, Brush> cache, string? tech, Func<Color, SolidColorBrush> make)
    {
        var key = tech ?? "";
        lock (cache)
        {
            if (cache.TryGetValue(key, out var brush)) return brush;
            var created = make(Color(tech));
            created.Freeze();
            cache[key] = created;
            return created;
        }
    }

    public static Color Color(string? tech) => tech switch
    {
        "Tauri" => FromHex("#FFC131"),
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

/// <summary>Renders a project's location as the folder breadcrumb below its root ("Tauri › Personal")
/// rather than the absolute path. Every project under one root shares the same long path prefix, so
/// showing it on a card wastes the line and truncates away the part that actually differs.</summary>
public sealed class ProjectBreadcrumbConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c) =>
        value is Project { Hierarchy.Count: > 0 } p ? string.Join("  ›  ", p.Hierarchy) : "root";
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Colours a terminal line by what it is. Painting the whole buffer one shade of green made
/// the echoed command indistinguishable from the output it produced.</summary>
public sealed class TerminalLineBrushConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c)
    {
        var line = value as string ?? "";
        if (line.StartsWith("$ ", StringComparison.Ordinal)) return Brush("AccentBrush");
        if (line.StartsWith('[') && line.EndsWith(']')) return Brush("FaintBrush");
        return Brush("TextDimBrush");
    }
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) => Binding.DoNothing;
    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}

/// <summary>Turns an available width into a column count so the settings page flows into two columns
/// once there is room, collapsing back to one on a narrow window. Parameter is the width at which the
/// second column earns its place (defaults to 820).</summary>
public sealed class ColumnCountConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c)
    {
        var width = value is double d ? d : 0;
        var threshold = double.TryParse(parameter?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : 820;
        return width >= threshold ? 2 : 1;
    }
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Explains the status dot. A coloured dot with no tooltip is a puzzle, not an indicator.</summary>
public sealed class ProjectStatusTooltipConverter : IValueConverter
{
    public object? Convert(object? value, Type t, object? parameter, CultureInfo c)
    {
        if (value is not Project p) return null;
        if (p.IsRunning) return p.RunningSessionCount == 1 ? "1 terminal session running" : $"{p.RunningSessionCount} terminal sessions running";
        if (p.HasGitChanges) return p.Git!.ModifiedFileCount == 1 ? "1 uncommitted change" : $"{p.Git.ModifiedFileCount} uncommitted changes";
        return null;
    }
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) => Binding.DoNothing;
}
