using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Puppeteer.Core;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace Puppeteer.App.Converters;

/// <summary>Resolves a project (or technology name) to a brand icon drawn from the bundled SVG set.</summary>
internal static class TechIcons
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    // Technology / badge name -> icon file name (without extension) under Assets/icons.
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        [".NET"] = "netcore", ["C#"] = "csharp", ["CSharp"] = "csharp", ["WPF"] = "netcore", ["Blazor"] = "netcore",
        ["Tauri"] = "tauri", ["Rust"] = "rust", ["TypeScript"] = "typescript", ["JavaScript"] = "javascript",
        ["React"] = "react", ["React Native"] = "reactnative", ["Redux"] = "redux",
        ["Flutter"] = "flutter", ["Dart"] = "dart", ["Riverpod"] = "flutter",
        ["Next.js"] = "nextjs", ["Vue"] = "vuejs", ["Vite"] = "vitejs", ["VitePress"] = "vitejs",
        ["Node.js"] = "nodejs", ["Node"] = "nodejs", ["Express"] = "expressjs", ["Deno"] = "deno", ["Bun"] = "bunjs",
        ["Qt"] = "qt", ["QML"] = "qt", ["C++"] = "c++", ["C"] = "c",
        ["Kotlin"] = "kotlin", ["Android"] = "android", ["Java"] = "java",
        ["Python"] = "python", ["Django"] = "django", ["Flask"] = "flask", ["FastAPI"] = "fastapi",
        ["Godot"] = "godot", ["Unity"] = "unity", ["Unreal"] = "unrealengine",
        ["Tailwind"] = "tailwindcss", ["Sass"] = "sass", ["CSS"] = "css3", ["HTML"] = "html5",
        ["Framer"] = "framer", ["Motion"] = "motion", ["GSAP"] = "gsap2", ["Three.js"] = "threejs",
        ["MDX"] = "markdown", ["Markdown"] = "markdown", ["Electron"] = "electron", ["Expo"] = "expo",
        ["Go"] = "go", ["PHP"] = "php", ["Laravel"] = "laravel", ["Ruby on Rails"] = "rails",
        ["GraphQL"] = "graphql", ["Prisma"] = "prisma", ["Firebase"] = "firebase", ["Supabase"] = "supabase",
        ["PostgreSQL"] = "postgresql", ["MySQL"] = "mysql", ["MongoDB"] = "mongodb", ["SQLite"] = "sqlite",
        ["Redis"] = "redis", ["Docker"] = "docker", ["SolidJS"] = "solidjs", ["WebAssembly"] = "webassembly",
    };

    public static ImageSource? Resolve(Project project)
    {
        if (!string.IsNullOrWhiteSpace(project.CustomIconPath))
            return LoadAny(project.CustomIconPath!);
        return ForTechnology(project.PrimaryTechnology);
    }

    public static ImageSource? ForTechnology(string? technology)
    {
        if (technology is null || !Map.TryGetValue(technology, out var file)) return null;
        return LoadSvg($"pack://application:,,,/Assets/icons/{file}.svg");
    }

    public static ImageSource? LoadAny(string path)
        => path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? LoadSvg(path) : LoadRaster(path);

    private static ImageSource? LoadRaster(string path)
    {
        if (Cache.TryGetValue(path, out var cached)) return cached;
        ImageSource? image = null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path, UriKind.RelativeOrAbsolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            image = bmp;
        }
        catch { }
        Cache[path] = image;
        return image;
    }

    private static ImageSource? LoadSvg(string pathOrPackUri)
    {
        if (Cache.TryGetValue(pathOrPackUri, out var cached)) return cached;
        ImageSource? image = null;
        try
        {
            Stream? stream = null;
            if (File.Exists(pathOrPackUri)) stream = File.OpenRead(pathOrPackUri);
            else stream = Application.GetResourceStream(new Uri(pathOrPackUri, UriKind.Absolute))?.Stream;
            if (stream is not null)
            {
                var settings = new WpfDrawingSettings { IncludeRuntime = true, TextAsGeometry = true, OptimizePath = true };
                using var reader = new FileSvgReader(settings);
                using (stream)
                {
                var drawing = reader.Read(stream);
                if (drawing is not null)
                {
                    image = new DrawingImage(drawing);
                    image.Freeze();
                }
                }
            }
        }
        catch { }
        Cache[pathOrPackUri] = image;
        return image;
    }
}

public sealed class ProjectIconSourceConverter : IValueConverter
{
    public object? Convert(object? value, Type t, object? parameter, CultureInfo c) =>
        value is Project p ? TechIcons.Resolve(p) : null;
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Uses a custom icon's dominant color as a subtle background gradient, while framework
/// icons retain the established technology tint.</summary>
public sealed class ProjectIconTintConverter : IValueConverter
{
    private static readonly Dictionary<string, Brush> CustomTintCache = new(StringComparer.OrdinalIgnoreCase);

    public object Convert(object? value, Type t, object? parameter, CultureInfo c)
    {
        if (value is not Project project || string.IsNullOrWhiteSpace(project.CustomIconPath)) return Tech.TintBrush((value as Project)?.PrimaryTechnology);
        var path = project.CustomIconPath;
        lock (CustomTintCache)
        {
            if (CustomTintCache.TryGetValue(path, out var cached)) return cached;
            var color = TryReadDominantColor(path) ?? Tech.Color(project.PrimaryTechnology);
            var gradient = new LinearGradientBrush(
                Color.FromArgb(104, color.R, color.G, color.B),
                Color.FromArgb(35, color.R, color.G, color.B), 45);
            gradient.Freeze();
            CustomTintCache[path] = gradient;
            return gradient;
        }
    }

    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) => Binding.DoNothing;

    private static Color? TryReadDominantColor(string path)
    {
        try
        {
            if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(File.ReadAllText(path), "#[0-9a-fA-F]{6}");
                return match.Success ? (Color)ColorConverter.ConvertFromString(match.Value) : null;
            }

            var source = new BitmapImage();
            source.BeginInit();
            source.UriSource = new Uri(path, UriKind.RelativeOrAbsolute);
            source.DecodePixelWidth = 64;
            source.DecodePixelHeight = 64;
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.EndInit();
            var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            double red = 0, green = 0, blue = 0, weight = 0;
            for (var i = 0; i < pixels.Length; i += 4)
            {
                var b = pixels[i]; var g = pixels[i + 1]; var r = pixels[i + 2]; var alpha = pixels[i + 3];
                var saturation = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
                var pixelWeight = alpha / 255d * (0.15 + saturation / 255d);
                red += r * pixelWeight; green += g * pixelWeight; blue += b * pixelWeight; weight += pixelWeight;
            }
            return weight > 0 ? Color.FromRgb((byte)(red / weight), (byte)(green / weight), (byte)(blue / weight)) : null;
        }
        catch { return null; }
    }
}

public sealed class TechnologyIconSourceConverter : IValueConverter
{
    public object? Convert(object? value, Type t, object? parameter, CultureInfo c) =>
        TechIcons.ForTechnology(value?.ToString());
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) => Binding.DoNothing;
}

public sealed class IconPathSourceConverter : IValueConverter
{
    public object? Convert(object? value, Type t, object? parameter, CultureInfo c) =>
        value is string path ? TechIcons.LoadAny(path) : null;
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Visible when a project has no resolvable brand icon (so a monogram fallback can show).</summary>
public sealed class ProjectIconFallbackVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c) =>
        value is Project p && TechIcons.Resolve(p) is not null ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) => Binding.DoNothing;
}
