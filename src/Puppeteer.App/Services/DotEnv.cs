using System.IO;

namespace Puppeteer.App.Services;

/// <summary>Holds values loaded from a developer <c>.env</c> file. These are the fallback used in
/// development; a key saved in Settings always takes precedence.</summary>
public sealed class AppConfig
{
    public string? GroqApiKeyFromEnv { get; init; }
}

public static class DotEnv
{
    /// <summary>Walks up from the app's base directory looking for a <c>.env</c> and parses simple
    /// <c>KEY=VALUE</c> lines. Missing file is fine — it just yields an empty config.</summary>
    public static AppConfig Load()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && directory is not null; i++, directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, ".env");
            if (!File.Exists(path)) continue;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var separator = line.IndexOf('=');
                if (separator <= 0) continue;
                var key = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim().Trim('"', '\'');
                if (!values.ContainsKey(key)) values[key] = value;
            }
            break;
        }

        return new AppConfig
        {
            GroqApiKeyFromEnv = values.TryGetValue("GROQ_API_KEY", out var groqKey) && groqKey.Length > 0 ? groqKey : null
        };
    }
}
