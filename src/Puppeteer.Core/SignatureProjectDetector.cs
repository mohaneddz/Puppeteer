using System.Text.Json;

namespace Puppeteer.Core;

public sealed class SignatureProjectDetector : IProjectDetector
{
    public async Task<ProjectDetectionResult?> DetectAsync(string directory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(directory)) return null;

        var technologies = new List<string>();
        var presets = new List<CommandPreset>();
        string? primary = null;
        void Add(string technology) { if (!technologies.Contains(technology, StringComparer.OrdinalIgnoreCase)) technologies.Add(technology); }
        void Detect(bool signal, string technology, params CommandPreset[] commands)
        {
            if (!signal) return;
            primary ??= technology;
            Add(technology);
            presets.AddRange(commands.Where(c => presets.All(p => p.Command != c.Command)));
        }

        var files = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly).Select(Path.GetFileName).Where(x => x is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tauri = File.Exists(Path.Combine(directory, "src-tauri", "tauri.conf.json"));
        Detect(tauri, "Tauri", new("Dev", "pnpm tauri dev"), new("Build", "pnpm tauri build"));
        if (tauri) Add("Rust");
        Detect(files.Any(x => x!.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)), ".NET",
            new("Run", "dotnet run"), new("Build", "dotnet build"), new("Test", "dotnet test"));
        if (files.Any(x => x!.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))) Add("C#");
        if (files.Any(x => x!.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) && Directory.Exists(Path.Combine(directory, "Views"))) Add("WPF");
        Detect(files.Contains("pubspec.yaml"), "Flutter", new("Run", "flutter run"), new("Test", "flutter test"), new("Packages", "flutter pub get"));
        Detect(files.Any(x => x!.StartsWith("next.config.", StringComparison.OrdinalIgnoreCase)), "Next.js");
        Detect(files.Any(x => x!.StartsWith("vite.config.", StringComparison.OrdinalIgnoreCase)), "Vite");
        Detect(files.Contains("CMakeLists.txt") || files.Any(x => x!.EndsWith(".pro", StringComparison.OrdinalIgnoreCase)), "Qt");
        Detect(files.Contains("build.gradle") || files.Contains("build.gradle.kts"), "Android");
        Detect(files.Contains("Cargo.toml"), "Rust", new("Run", "cargo run"), new("Build", "cargo build"), new("Test", "cargo test"));
        Detect(files.Contains("pyproject.toml") || files.Contains("requirements.txt"), "Python", new("Run", "python ."), new("Test", "pytest"));
        Detect(files.Contains("project.godot"), "Godot");

        var packagePath = Path.Combine(directory, "package.json");
        if (File.Exists(packagePath))
        {
            Add("Node.js");
            try
            {
                await using var stream = File.OpenRead(packagePath);
                using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                var text = json.RootElement.GetRawText();
                if (text.Contains("react", StringComparison.OrdinalIgnoreCase)) Add("React");
                if (text.Contains("typescript", StringComparison.OrdinalIgnoreCase)) Add("TypeScript");
                if (primary is null) primary = "Node.js";
                if (json.RootElement.TryGetProperty("scripts", out var scripts))
                    foreach (var script in scripts.EnumerateObject().Take(6)) presets.Add(new(script.Name, $"npm run {script.Name}"));
            }
            catch (JsonException) { /* Invalid manifests do not prevent indexing. */ }
        }

        return primary is null ? null : new(primary, technologies, presets);
    }
}
