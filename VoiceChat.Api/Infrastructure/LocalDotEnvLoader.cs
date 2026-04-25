using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace VoiceChat.Api.Infrastructure;

/// <summary>Resolves <c>{{Section:Key}}</c> (e.g. Google placeholders from other config keys).</summary>
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
/// Skips blank values so an empty <c>KEY=</c> line does not clear keys already set by the host.
/// Runs when a candidate <c>.env</c> file exists (git-ignored). Use the same names as process environment variables (e.g. <c>Google__ClientId</c>).
/// </summary>
public static class LocalDotEnvLoader
{
    /// <summary>Loads the first <c>.env</c> found into <see cref="Environment"/> (same <c>KEY=value</c> names as hosting env vars).</summary>
    public static void TryLoad()
    {
        foreach (var path in GetCandidateEnvFilePaths())
        {
            if (!File.Exists(path))
                continue;

            foreach (var raw in File.ReadLines(path))
            {
                if (!TryParseDotEnvLine(raw, out var key, out var value))
                    continue;

                // Never overwrite host-provided secrets with an empty placeholder from .env (e.g. copied .env.example).
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                Environment.SetEnvironmentVariable(key, value);
            }

            break;
        }
    }

    /// <summary>
    /// Pushes the first <c>.env</c> into <paramref name="configuration"/> so keys override <c>appsettings.json</c>
    /// (env-style <c>KEY__Nested=value</c>; <c>__</c> becomes <c>:</c> in configuration keys).
    /// </summary>
    public static void MergeIntoConfiguration(ConfigurationManager configuration)
    {
        foreach (var path in GetCandidateEnvFilePaths())
        {
            if (!File.Exists(path))
                continue;

            var batch = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in File.ReadLines(path))
            {
                if (!TryParseDotEnvLine(raw, out var key, out var value))
                    continue;
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                batch[key.Replace("__", ":", StringComparison.Ordinal)] = value;
            }

            if (batch.Count > 0)
                configuration.AddInMemoryCollection(batch);

            break;
        }
    }

    private static bool TryParseDotEnvLine(string raw, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;

        var line = raw.Trim();
        if (line.Length == 0 || line.StartsWith('#'))
            return false;

        if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
            line = line[7..].TrimStart();

        var eq = line.IndexOf('=');
        if (eq <= 0)
            return false;

        key = line[..eq].Trim();
        if (key.Length == 0)
            return false;

        value = StripOptionalQuotes(line[(eq + 1)..].Trim());
        return true;
    }

    private static IEnumerable<string> GetCandidateEnvFilePaths()
    {
        yield return Path.Combine(Directory.GetCurrentDirectory(), ".env");
        // When Visual Studio uses the repo root as working directory (e.g. d:\ChatbotAI).
        yield return Path.Combine(Directory.GetCurrentDirectory(), "Api", "VoiceChat.Api", ".env");
        yield return Path.Combine(AppContext.BaseDirectory, ".env");

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
