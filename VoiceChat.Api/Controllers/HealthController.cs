using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using VoiceChat.Api.Options;
using VoiceChat.Api.Services;

namespace VoiceChat.Api.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/[controller]")]
public class HealthController(IHttpClientFactory httpClientFactory, IOptions<OllamaOptions> ollamaOptions) : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "ok", time = DateTimeOffset.UtcNow });

    /// <summary>Local Ollama LLM — no paid cloud API.</summary>
    [HttpGet("llm")]
    public IActionResult Llm()
    {
        var opts = ollamaOptions.Value;
        var baseUrl = opts.BaseUrl?.Trim().Length > 0 ? opts.BaseUrl.Trim() : OllamaOptions.DefaultLocalBaseUrl.TrimEnd('/');
        return Ok(new
        {
            provider = "Ollama",
            defaultModel = LlmRuntime.DefaultChatModel(opts),
            baseUrl,
            hint =
                "Install Ollama from https://ollama.com, run ollama pull <model>, then chat. " +
                "GET /api/health/ollama/models lists pulled models."
        });
    }

    /// <summary>Lists model names available on your Ollama server (same as <c>ollama list</c>).</summary>
    [HttpGet("ollama/models")]
    public async Task<IActionResult> OllamaModels(CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("OllamaHealth");
        try
        {
            using var response = await client.GetAsync("api/tags", cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Ok(new
                {
                    ok = false,
                    statusCode = (int)response.StatusCode,
                    message = json
                });
            }

            var names = new List<string>();
            using (var doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.TryGetProperty("models", out var models))
                {
                    foreach (var m in models.EnumerateArray())
                    {
                        if (m.TryGetProperty("name", out var nameEl))
                        {
                            var n = nameEl.GetString();
                            if (!string.IsNullOrEmpty(n))
                                names.Add(n);
                        }
                    }
                }
            }

            return Ok(new { ok = true, models = names });
        }
        catch (Exception ex)
        {
            return Ok(new { ok = false, error = ex.Message });
        }
    }
}
