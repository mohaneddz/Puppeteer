using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Puppeteer.Core;

namespace Puppeteer.Infrastructure;

/// <summary>Asks Groq's (OpenAI-compatible) chat API to label a project from its README and path.
/// Falls back to the offline path heuristic when the model is unavailable or unsure.</summary>
public sealed class GroqProjectClassifier(HttpClient httpClient) : IProjectClassifier
{
    private const string Endpoint = "https://api.groq.com/openai/v1/chat/completions";
    private const string Model = "llama-3.1-8b-instant";

    public async Task<string?> ClassifyAsync(string projectPath, string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return ProjectCategoryRules.FromPath(projectPath);

        var readme = ReadReadme(projectPath);
        var allowed = string.Join(", ", ProjectCategoryRules.Known);
        var prompt = $"""
            Classify this software project into exactly one category from: {allowed}.
            Personal = the author's own hobby/side project. Client = built for a paying client or freelance.
            Hackathon = made for a hackathon/game jam. Course = coursework, tutorials, or learning exercises.
            Work = built for an employer. Other = none of these fit.
            Answer with a single word from the list and nothing else.

            Path: {projectPath}
            README:
            {(string.IsNullOrWhiteSpace(readme) ? "(none)" : readme)}
            """;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            request.Headers.Add("Authorization", $"Bearer {apiKey}");
            request.Content = JsonContent.Create(new
            {
                model = Model,
                temperature = 0,
                max_tokens = 4,
                messages = new[] { new { role = "user", content = prompt } }
            });

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return ProjectCategoryRules.FromPath(projectPath);

            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var content = document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            return Normalize(content) ?? ProjectCategoryRules.FromPath(projectPath);
        }
        catch
        {
            return ProjectCategoryRules.FromPath(projectPath);
        }
    }

    private static string? Normalize(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return null;
        var cleaned = answer.Trim().Trim('.', ',', '"', '\'', ' ');
        return ProjectCategoryRules.Known.FirstOrDefault(k => k.Equals(cleaned, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ReadReadme(string projectPath)
    {
        try
        {
            var readme = Directory.EnumerateFiles(projectPath, "README*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals("README", StringComparison.OrdinalIgnoreCase));
            if (readme is null) return null;
            var text = File.ReadAllText(readme);
            return text.Length > 1600 ? text[..1600] : text;
        }
        catch { return null; }
    }
}
