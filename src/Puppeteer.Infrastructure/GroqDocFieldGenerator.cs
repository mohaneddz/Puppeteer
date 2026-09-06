using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Puppeteer.Core;

namespace Puppeteer.Infrastructure;

/// <summary>Asks Groq's (OpenAI-compatible) chat API to write one section of a project's state doc,
/// grounded in the project's own README and file layout rather than invented from the project name.</summary>
public sealed class GroqDocFieldGenerator(HttpClient httpClient) : IDocFieldGenerator
{
    private const string Endpoint = "https://api.groq.com/openai/v1/chat/completions";
    private const string Model = "llama-3.3-70b-versatile";
    private const int MaxTreeEntries = 150;
    private static readonly HashSet<string> IgnoredEntries = new(StringComparer.OrdinalIgnoreCase)
        { "node_modules", ".git", "bin", "obj", "dist", "build", "target", ".vs", ".idea", "packages", ".next", "out" };

    public async Task<string?> GenerateAsync(string section, string projectName, string projectPath, string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        var readme = ReadReadme(projectPath);
        var tree = BuildTree(projectPath);
        var prompt = $"""
            You are writing one section of a developer's own working notes about their project — plain,
            factual, not marketing copy. {Instruction(section)}
            Base your answer only on the evidence below; do not invent features, frameworks or files that
            are not shown. Markdown bullet points where a list fits, otherwise plain prose. Under 120 words.

            Project: {projectName}

            README:
            {(string.IsNullOrWhiteSpace(readme) ? "(none)" : readme)}

            File structure:
            {(tree.Length == 0 ? "(could not be read)" : tree)}
            """;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            request.Headers.Add("Authorization", $"Bearer {apiKey}");
            request.Content = JsonContent.Create(new
            {
                model = Model,
                temperature = 0.4,
                max_tokens = 400,
                messages = new[] { new { role = "user", content = prompt } }
            });

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var content = document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            return string.IsNullOrWhiteSpace(content) ? null : content.Trim();
        }
        catch { return null; }
    }

    private static string Instruction(string section) => section switch
    {
        ProjectDocSections.Summary => "Write a one to two sentence summary of what this project is and what it does.",
        ProjectDocSections.Works => "List what appears implemented and working, based on the file structure and README. Name actual files, flows or modules where you can.",
        ProjectDocSections.Broken => "List what looks unfinished, missing or likely broken (TODO markers, stub files, empty folders, gaps between the README's claims and the structure). If nothing stands out, say so in one line.",
        ProjectDocSections.Next => "List sensible next steps for this project given its current state.",
        ProjectDocSections.Notes => "List any implementation caveats, gotchas or non-obvious decisions worth remembering about this project.",
        _ => "Write a short, factual note about this project.",
    };

    private static string? ReadReadme(string projectPath)
    {
        try
        {
            var readme = Directory.EnumerateFiles(projectPath, "README*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals("README", StringComparison.OrdinalIgnoreCase));
            if (readme is null) return null;
            var text = File.ReadAllText(readme);
            return text.Length > 3000 ? text[..3000] : text;
        }
        catch { return null; }
    }

    /// <summary>A shallow, capped file tree — enough for the model to see the project's shape without
    /// shipping its entire source as a prompt.</summary>
    private static string BuildTree(string projectPath)
    {
        var lines = new List<string>();
        try { Walk(projectPath, 0, lines); } catch { }
        return string.Join('\n', lines);
    }

    private static void Walk(string dir, int depth, List<string> lines)
    {
        if (depth > 3 || lines.Count >= MaxTreeEntries) return;
        IEnumerable<string> entries;
        try { entries = Directory.EnumerateFileSystemEntries(dir).OrderBy(e => e, StringComparer.OrdinalIgnoreCase); }
        catch { return; }
        foreach (var entry in entries)
        {
            if (lines.Count >= MaxTreeEntries) return;
            var name = Path.GetFileName(entry);
            if (IgnoredEntries.Contains(name)) continue;
            var isDirectory = Directory.Exists(entry);
            lines.Add($"{new string(' ', depth * 2)}{name}{(isDirectory ? "/" : "")}");
            if (isDirectory) Walk(entry, depth + 1, lines);
        }
    }
}
