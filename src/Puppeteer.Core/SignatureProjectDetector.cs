using System.Text.Json;
using System.Xml.Linq;

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
        Detect(files.Any(x => x!.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
            || x.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
            || x.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)), ".NET",
            new("Run", "dotnet run"), new("Build", "dotnet build"), new("Test", "dotnet test"));
        if (files.Any(x => x!.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))) Add("C#");
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
                foreach (var group in new[] { "dependencies", "devDependencies", "peerDependencies" })
                    if (json.RootElement.TryGetProperty(group, out var dependencies) && dependencies.ValueKind == JsonValueKind.Object)
                        foreach (var dependency in dependencies.EnumerateObject())
                            if (PackageTechnologies.TryGetValue(dependency.Name, out var technology)) Add(technology);
                if (primary is null) primary = "Node.js";
                if (json.RootElement.TryGetProperty("scripts", out var scripts))
                    foreach (var script in scripts.EnumerateObject().Take(6)) presets.Add(new(script.Name, $"npm run {script.Name}"));
            }
            catch (JsonException) { /* Invalid manifests do not prevent indexing. */ }
        }

        // Enrich recognized projects only: recursively probing a library/root folder would merge
        // unrelated projects and prevent the scanner from discovering them individually.
        if (primary is null) return null;
        foreach (var file in SourceFiles(directory, cancellationToken))
        {
            var extension = Path.GetExtension(file);
            if (SourceTechnologies.TryGetValue(extension, out var language)) Add(language);
            if (extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                Add("C#");
                try
                {
                    var project = XDocument.Load(file);
                    bool Enabled(string property) => project.Descendants().Any(e => e.Name.LocalName == property && e.Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase));
                    if (Enabled("UseWPF")) Add("WPF");
                    if (Enabled("UseWindowsForms")) Add("Windows Forms");
                    if (Enabled("UseMaui")) Add("MAUI");
                    var sdk = project.Root?.Attribute("Sdk")?.Value ?? "";
                    if (sdk.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase)) Add("ASP.NET Core");
                    if (sdk.Contains("BlazorWebAssembly", StringComparison.OrdinalIgnoreCase)) Add("Blazor");
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException) { }
            }
        }
        return new(primary, technologies, presets);
    }

    private static readonly Dictionary<string, string> PackageTechnologies = new(StringComparer.OrdinalIgnoreCase)
    {
        ["react"] = "React", ["react-dom"] = "React", ["typescript"] = "TypeScript",
        ["vue"] = "Vue", ["svelte"] = "Svelte", ["@angular/core"] = "Angular",
        ["next"] = "Next.js", ["vite"] = "Vite", ["tailwindcss"] = "Tailwind",
        ["sass"] = "Sass", ["electron"] = "Electron", ["express"] = "Express",
    };

    private static readonly Dictionary<string, string> SourceTechnologies = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "C#", [".fs"] = "F#", [".fsproj"] = "F#", [".vb"] = "Visual Basic",
        [".vbproj"] = "Visual Basic", [".rs"] = "Rust", [".dart"] = "Dart",
        [".html"] = "HTML", [".htm"] = "HTML", [".css"] = "CSS", [".scss"] = "Sass", [".sass"] = "Sass",
        [".js"] = "JavaScript", [".jsx"] = "JavaScript", [".mjs"] = "JavaScript", [".cjs"] = "JavaScript",
        [".ts"] = "TypeScript", [".tsx"] = "TypeScript", [".vue"] = "Vue", [".svelte"] = "Svelte",
        [".py"] = "Python", [".java"] = "Java", [".kt"] = "Kotlin", [".swift"] = "Swift",
        [".c"] = "C", [".cpp"] = "C++", [".cc"] = "C++", [".go"] = "Go", [".razor"] = "Blazor",
    };

    private static IEnumerable<string> SourceFiles(string root, CancellationToken token)
    {
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((root, 0));
        var remaining = 10000;
        while (queue.Count > 0 && remaining > 0)
        {
            token.ThrowIfCancellationRequested();
            var (path, depth) = queue.Dequeue();
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            foreach (var entry in entries)
            {
                token.ThrowIfCancellationRequested();
                if (--remaining < 0) yield break;
                FileAttributes attributes;
                try { attributes = File.GetAttributes(entry); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
                if (attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                if (!attributes.HasFlag(FileAttributes.Directory)) { yield return entry; continue; }
                var name = Path.GetFileName(entry);
                if (depth < 8 && !ProjectPathRules.IsIgnoredDirectory(entry)
                    && !name.StartsWith('.') && !ExcludedSourceFolders.Contains(name)) queue.Enqueue((entry, depth + 1));
            }
        }
    }

    private static readonly HashSet<string> ExcludedSourceFolders = new(StringComparer.OrdinalIgnoreCase)
        { "bin", "obj", "target", "dist", "build", "build-verification", "coverage", "vendor", "venv", "__pycache__", "node_modules" };
}
