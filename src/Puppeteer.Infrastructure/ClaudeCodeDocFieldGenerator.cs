using System.Diagnostics;
using Puppeteer.Core;

namespace Puppeteer.Infrastructure;

/// <summary>Asks a local, already-authenticated Claude Code CLI to write one section of a project's
/// state doc. Unlike <see cref="GroqDocFieldGenerator"/> it does not pre-build a file tree or read the
/// README itself — it points Claude Code at the project directory and lets it look around.</summary>
public sealed class ClaudeCodeDocFieldGenerator : IDocFieldGenerator
{
    private const string Model = "claude-haiku-4-5-20251001";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    public async Task<string?> GenerateAsync(string section, string projectName, string projectPath, string apiKey, CancellationToken cancellationToken = default)
    {
        var prompt = $"""
            You are writing one section of a developer's own working notes about the project in your
            current working directory — plain, factual, not marketing copy. {Instruction(section)}
            Inspect the project's files to find real evidence; do not invent features, frameworks or
            files you have not seen, and do not edit or create anything. Markdown bullet points where a
            list fits, otherwise plain prose. Under 120 words. Reply with only the section body — no
            heading, no preamble, no code fences.

            Project: {projectName}
            """;

        var info = new ProcessStartInfo("claude")
        {
            WorkingDirectory = projectPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add("-p");
        info.ArgumentList.Add(prompt);
        info.ArgumentList.Add("--model");
        info.ArgumentList.Add(Model);
        info.ArgumentList.Add("--output-format");
        info.ArgumentList.Add("text");
        info.ArgumentList.Add("--allowedTools");
        info.ArgumentList.Add("Read,Glob,Grep");
        info.ArgumentList.Add("--permission-mode");
        info.ArgumentList.Add("dontAsk");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(Timeout);

        using var process = Process.Start(info);
        if (process is null) return null;
        try
        {
            var output = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            if (process.ExitCode != 0) return null;
            return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            return null;
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
}
