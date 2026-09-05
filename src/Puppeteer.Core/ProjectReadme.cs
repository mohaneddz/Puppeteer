namespace Puppeteer.Core;

/// <summary>Finds a project's own README — the other markdown worth reading about it, alongside the
/// state doc kept in the vault.</summary>
public static class ProjectReadme
{
    private static readonly string[] Names = ["README.md", "readme.md", "Readme.md", "README.markdown", "readme.markdown", "README.MD"];

    public static string? Find(string projectPath)
    {
        try
        {
            if (!Directory.Exists(projectPath)) return null;
            foreach (var name in Names)
            {
                var candidate = Path.Combine(projectPath, name);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }
}
