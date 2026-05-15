using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using VoiceChat.Api.Options;
using VoiceChat.Api.Services;

namespace VoiceChat.Api.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/[controller]")]
public class HealthController(
    IOptions<GeminiOptions> geminiOptions,
    IOptions<OllamaOptions> ollamaOptions) : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "ok", time = DateTimeOffset.UtcNow });

    /// <summary>Configured Gemini + Ollama defaults.</summary>
    [HttpGet("llm")]
    public IActionResult Llm()
    {
        var opts = geminiOptions.Value;
        var ollama = ollamaOptions.Value;
        return Ok(new
        {
            providers = new[] { "Gemini", "Ollama" },
            defaultModel = LlmRuntime.DefaultChatModel(opts),
            gemini = new
            {
                baseUrl = opts.ResolveBaseUri().ToString().TrimEnd('/'),
                configured = !string.IsNullOrWhiteSpace(opts.ApiKey),
                models = GetConfiguredModels(opts)
            },
            ollama = new
            {
                baseUrl = (ollama.BaseUrl ?? string.Empty).TrimEnd('/'),
                defaultModel = $"ollama:{(string.IsNullOrWhiteSpace(ollama.DefaultModel) ? "qwen2.5-coder:14b" : ollama.DefaultModel.Trim())}",
                models = GetOllamaUiModels(ollama)
            },
            models = MergeModelLists(GetConfiguredModels(opts), GetOllamaUiModels(ollama)),
            maxHistoryMessages = Math.Max(4, opts.MaxHistoryMessages),
            hint =
                "Set Gemini__ApiKey for cloud chat, or run Ollama locally and pick ollama:qwen2.5-coder:14b. GET /api/health/gemini/models lists all configured choices."
        });
    }

    /// <summary>Lists Gemini and Ollama model ids for the UI dropdown.</summary>
    [HttpGet("gemini/models")]
    public IActionResult GeminiModels()
    {
        var opts = geminiOptions.Value;
        var ollama = ollamaOptions.Value;
        var models = MergeModelLists(GetConfiguredModels(opts), GetOllamaUiModels(ollama));
        return Ok(new
        {
            ok = true,
            defaultModel = LlmRuntime.DefaultChatModel(opts),
            models,
            maxHistoryMessages = Math.Max(4, opts.MaxHistoryMessages)
        });
    }

    private static IReadOnlyList<string> GetConfiguredModels(GeminiOptions opts)
    {
        var models = opts.AvailableModels
            .Select(m => LlmRuntime.NormalizeChatModel(m, opts))
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var fallback = LlmRuntime.DefaultChatModel(opts);
        if (!models.Contains(fallback, StringComparer.OrdinalIgnoreCase))
            models.Insert(0, fallback);

        return models;
    }

    private static IReadOnlyList<string> GetOllamaUiModels(OllamaOptions opts)
    {
        var names = opts.AvailableModels?
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim())
            .ToList() ?? [];

        if (names.Count == 0 && !string.IsNullOrWhiteSpace(opts.DefaultModel))
            names = [opts.DefaultModel.Trim()];

        return names
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(m => m.StartsWith("ollama:", StringComparison.OrdinalIgnoreCase) ? m : $"ollama:{m}")
            .ToList();
    }

    private static IReadOnlyList<string> MergeModelLists(
        IReadOnlyList<string> gemini,
        IReadOnlyList<string> ollama)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in gemini)
            set.Add(m);
        foreach (var m in ollama)
            set.Add(m);
        return set.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
