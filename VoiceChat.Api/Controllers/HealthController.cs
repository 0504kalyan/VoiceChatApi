using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using VoiceChat.Api.Options;
using VoiceChat.Api.Services;

namespace VoiceChat.Api.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/[controller]")]
public class HealthController(IOptions<GeminiOptions> geminiOptions) : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "ok", time = DateTimeOffset.UtcNow });

    /// <summary>Configured Gemini LLM provider.</summary>
    [HttpGet("llm")]
    public IActionResult Llm()
    {
        var opts = geminiOptions.Value;
        return Ok(new
        {
            provider = "Gemini",
            defaultModel = LlmRuntime.DefaultChatModel(opts),
            baseUrl = opts.ResolveBaseUri().ToString().TrimEnd('/'),
            configured = !string.IsNullOrWhiteSpace(opts.ApiKey),
            models = GetConfiguredModels(opts),
            maxHistoryMessages = Math.Max(4, opts.MaxHistoryMessages),
            hint =
                "Set Gemini__ApiKey on the API server. GET /api/health/gemini/models lists configured model choices."
        });
    }

    /// <summary>Lists Gemini model names configured for the UI dropdown.</summary>
    [HttpGet("gemini/models")]
    public IActionResult GeminiModels()
    {
        var opts = geminiOptions.Value;
        return Ok(new
        {
            ok = true,
            defaultModel = LlmRuntime.DefaultChatModel(opts),
            models = GetConfiguredModels(opts),
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
}
