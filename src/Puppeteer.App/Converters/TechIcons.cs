using System.Globalization;
using System.IO;
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

    private static ImageSource? LoadAny(string path)
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

    private static ImageSource? LoadSvg(string packUri)
    {
        if (Cache.TryGetValue(packUri, out var cached)) return cached;
        ImageSource? image = null;
        try
        {
            var info = Application.GetResourceStream(new Uri(packUri, UriKind.Absolute));
            if (info is not null)
            {
                var settings = new WpfDrawingSettings { IncludeRuntime = true, TextAsGeometry = true, OptimizePath = true };
                using var reader = new FileSvgReader(settings);
                using var stream = info.Stream;
                var drawing = reader.Read(stream);
                if (drawing is not null)
                {
                    image = new DrawingImage(drawing);
                    image.Freeze();
                }
            }
        }
        catch { }
        Cache[packUri] = image;
        return image;
    }
}

public sealed class ProjectIconSourceConverter : IValueConverter
{
    public object? Convert(object? value, Type t, object? parameter, CultureInfo c) =>
        value is Project p ? TechIcons.Resolve(p) : null;
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Visible when a project has no resolvable brand icon (so a monogram fallback can show).</summary>
public sealed class ProjectIconFallbackVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? parameter, CultureInfo c) =>
        value is Project p && TechIcons.Resolve(p) is not null ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c) => Binding.DoNothing;
}
