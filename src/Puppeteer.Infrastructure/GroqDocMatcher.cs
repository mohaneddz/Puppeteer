using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Puppeteer.Core;

namespace Puppeteer.Infrastructure;

/// <summary>Asks Groq's (OpenAI-compatible) chat API which doc describes each leftover project. The
/// model sees a numbered catalog of every doc (its name, Location, Stack and one-line summary) and a
/// batch of projects (name, path, detected stack, and the index's own guess), and answers with the
/// doc number for each — or -1 when nothing fits. A larger model is used than for classification
/// because this is a reasoning task: recognising that a doc whose Location is a parent folder still
/// describes the app in its subfolder.</summary>
public sealed class GroqDocMatcher(HttpClient httpClient) : IDocMatcher
{
    private const string Endpoint = "https://api.groq.com/openai/v1/chat/completions";
    private const string Model = "llama-3.3-70b-versatile";
    private const int ProjectsPerCall = 20;

    public async Task<IReadOnlyDictionary<Guid, string>> MatchAsync(
        IReadOnlyList<Project> projects,
        IReadOnlyList<ProjectDoc> docs,
        IReadOnlyDictionary<Guid, string> indexHints,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<Guid, string>();
        if (string.IsNullOrWhiteSpace(apiKey) || projects.Count == 0 || docs.Count == 0) return result;

        var catalog = BuildCatalog(docs);
        for (var start = 0; start < projects.Count; start += ProjectsPerCall)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = projects.Skip(start).Take(ProjectsPerCall).ToArray();
            try { await MatchBatchAsync(batch, docs, catalog, indexHints, apiKey, result, cancellationToken); }
            catch (OperationCanceledException) { throw; }
            catch { /* leave this batch unmatched; the heuristics already placed the rest */ }
        }
        return result;
    }

    private async Task MatchBatchAsync(IReadOnlyList<Project> batch, IReadOnlyList<ProjectDoc> docs, string catalog,
        IReadOnlyDictionary<Guid, string> indexHints, string apiKey, Dictionary<Guid, string> result, CancellationToken cancellationToken)
    {
        var projectList = new StringBuilder();
        for (var i = 0; i < batch.Count; i++)
        {
            var p = batch[i];
            projectList.Append(i).Append(": name=\"").Append(p.Name).Append("\" path=\"").Append(p.Path).Append('"');
            if (p.Technologies.Count > 0) projectList.Append(" stack=\"").Append(string.Join(", ", p.Technologies)).Append('"');
            if (indexHints.TryGetValue(p.Id, out var hint)) projectList.Append(" indexGuess=\"").Append(hint).Append('"');
            projectList.Append('\n');
        }

        var prompt = $$"""
            You match a software project to the one markdown doc that describes it.
            A doc's Location may be a parent folder while the project itself is a repo inside it (for
            example the app in an "App" subfolder) — that is still a match. A folder can be renamed
            after its doc was written, so trust the doc's Location, title and summary over an exact
            name equality. "indexGuess" is a hand-written hint; prefer it unless it is clearly wrong.

            DOCS (pick by number):
            {{catalog}}

            PROJECTS:
            {{projectList}}

            For every project number, give the number of the doc that describes it, or -1 if no doc fits.
            Answer with ONLY a JSON object mapping each project number (as a string) to a doc number.
            Example: {"0": 4, "1": -1, "2": 12}
            """;

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Add("Authorization", $"Bearer {apiKey}");
        request.Content = JsonContent.Create(new
        {
            model = Model,
            temperature = 0,
            max_tokens = 900,
            response_format = new { type = "json_object" },
            messages = new[] { new { role = "user", content = prompt } }
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return;

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var content = document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        if (string.IsNullOrWhiteSpace(content)) return;

        Apply(content, batch, docs, result);
    }

    private static void Apply(string json, IReadOnlyList<Project> batch, IReadOnlyList<ProjectDoc> docs, Dictionary<Guid, string> result)
    {
        JsonDocument parsed;
        try { parsed = JsonDocument.Parse(json); }
        catch (JsonException) { return; }
        using (parsed)
        {
            if (parsed.RootElement.ValueKind != JsonValueKind.Object) return;
            foreach (var pair in parsed.RootElement.EnumerateObject())
            {
                if (!int.TryParse(pair.Name, out var projectIndex) || projectIndex < 0 || projectIndex >= batch.Count) continue;
                if (!TryDocIndex(pair.Value, out var docIndex) || docIndex < 0 || docIndex >= docs.Count) continue;
                result[batch[projectIndex].Id] = docs[docIndex].FilePath;
            }
        }
    }

    private static bool TryDocIndex(JsonElement value, out int index)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Number when value.TryGetInt32(out index): return true;
            case JsonValueKind.String when int.TryParse(value.GetString(), out index): return true;
            default: index = -1; return false;
        }
    }

    private static string BuildCatalog(IReadOnlyList<ProjectDoc> docs)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < docs.Count; i++)
        {
            var doc = docs[i];
            builder.Append('[').Append(i).Append("] name=\"").Append(Path.GetFileName(doc.FilePath)).Append('"');
            if (doc.Location is { Length: > 0 } location) builder.Append(" location=\"").Append(location).Append('"');
            if (doc.Stack is { Length: > 0 } stack) builder.Append(" stack=\"").Append(Trim(stack, 80)).Append('"');
            if (doc.Section(ProjectDocSections.Summary) is { Length: > 0 } summary) builder.Append(" summary=\"").Append(Trim(FirstLine(summary), 110)).Append('"');
            builder.Append('\n');
        }
        return builder.ToString();
    }

    private static string FirstLine(string text) =>
        text.Replace("\r\n", "\n").Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..max].TrimEnd() + "…";
}
