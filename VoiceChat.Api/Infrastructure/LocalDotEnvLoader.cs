using Microsoft.Extensions.Configuration;

namespace VoiceChat.Api.Infrastructure;

/// <summary>
/// Resolves <c>{{Section:Key}}</c> (e.g. <c>{{GoogleCredentials:ClientId}}</c>) from other config keys so the same
/// env vars work locally (<c>.env</c>) and on Render (<c>GoogleCredentials__ClientId</c>, etc.).
/// </summary>
public static class ConfigurationPlaceholderExpander
{
    private const int MaxResolveDepth = 16;
    private const int MaxPasses = 8;

    public static void Apply(ConfigurationManager configuration)
    {
        for (var pass = 0; pass < MaxPasses; pass++)
        {
            var batch = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in configuration.AsEnumerable())
            {
                if (string.IsNullOrEmpty(kv.Key))
                    continue;
                var value = kv.Value;
                if (string.IsNullOrEmpty(value) || value.Length < 4)
                    continue;
                if (!value.StartsWith("{{", StringComparison.Ordinal) || !value.EndsWith("}}", StringComparison.Ordinal))
                    continue;

                var refPath = value[2..^2].Trim();
                if (refPath.Length == 0)
                    continue;

                var resolved = Resolve(configuration, refPath, 0);
                if (resolved is not null && resolved != value)
                    batch[kv.Key] = resolved;
            }

            if (batch.Count == 0)
                break;

            configuration.AddInMemoryCollection(batch);
        }

        // Older appsettings used SupabaseCredentials:DefaultConnection while ConnectionStrings:DefaultConnection
        // referenced {{SupabaseCredentials:ConnectionString}} — if that key was empty, Npgsql saw the literal "{{...}}"
        // and threw "starting at index 0". Copy from either Supabase key when DefaultConnection is missing or unreplaced.
        WarmDefaultConnectionFromSupabase(configuration);
    }

    private static void WarmDefaultConnectionFromSupabase(ConfigurationManager configuration)
    {
        var current = PostgresConnectionStringResolver.Normalize(configuration["ConnectionStrings:DefaultConnection"]);
        if (!string.IsNullOrWhiteSpace(current) && !PostgresConnectionStringResolver.IsUnresolvedPlaceholder(current))
            return;

        var resolved = PickFirstNonEmpty(
            PostgresConnectionStringResolver.Normalize(configuration["SupabaseCredentials:ConnectionString"]),
            PostgresConnectionStringResolver.Normalize(configuration["SupabaseCredentials:DefaultConnection"]));

        if (string.IsNullOrWhiteSpace(resolved))
            return;

        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = resolved,
            ["SupabaseCredentials:ConnectionString"] = resolved
        });
    }

    private static string? PickFirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v;
        }

        return null;
    }

    private static string? Resolve(IConfiguration configuration, string path, int depth)
    {
        if (depth > MaxResolveDepth)
            throw new InvalidOperationException(
                $"Configuration placeholder depth exceeded for path '{path}'. Check for circular {{...}} references.");

        var v = configuration[path];
        if (string.IsNullOrEmpty(v))
            return null;

        if (v.Length >= 4 && v.StartsWith("{{", StringComparison.Ordinal) && v.EndsWith("}}", StringComparison.Ordinal))
            return Resolve(configuration, v[2..^2].Trim(), depth + 1);

        return v;
    }
}

/// <summary>
/// Loads a <c>.env</c> file into the process environment before <see cref="WebApplication.CreateBuilder(string[])"/>.
/// Use the same keys as Render (e.g. <c>GoogleCredentials__ClientId</c>) — never commit <c>.env</c>.
/// </summary>
public static class LocalDotEnvLoader
{
    public static void TryLoad()
    {
        if (!ShouldLoadForCurrentEnvironment())
            return;

        foreach (var path in GetCandidateEnvFilePaths())
        {
            if (!File.Exists(path))
                continue;

            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
                    line = line[7..].TrimStart();

                var eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;

                var key = line[..eq].Trim();
                if (key.Length == 0)
                    continue;

                var value = line[(eq + 1)..].Trim();
                value = StripOptionalQuotes(value);

                Environment.SetEnvironmentVariable(key, value);
            }

            break;
        }
    }

    private static bool ShouldLoadForCurrentEnvironment()
    {
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        return string.IsNullOrEmpty(env) || env.Equals("Development", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetCandidateEnvFilePaths()
    {
        yield return Path.Combine(Directory.GetCurrentDirectory(), ".env");

        var asmPath = typeof(LocalDotEnvLoader).Assembly.Location;
        var dir = Path.GetDirectoryName(asmPath);
        if (string.IsNullOrEmpty(dir))
            yield break;

        var projectRoot = Path.GetFullPath(Path.Combine(dir, "..", "..", ".."));
        yield return Path.Combine(projectRoot, ".env");
    }

    private static string StripOptionalQuotes(string value)
    {
        if (value.Length < 2)
            return value;

        if ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
            return value[1..^1];

        return value;
    }
}
