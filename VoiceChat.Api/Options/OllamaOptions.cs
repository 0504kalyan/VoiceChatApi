namespace VoiceChat.Api.Options;

/// <summary>
/// Local Ollama configuration. Uses Ollama's OpenAI-compatible endpoint by default.
/// </summary>
public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; set; } = "http://localhost:11434/v1/";

    public string DefaultModel { get; set; } = "qwen2.5-coder:14b";

    public double? Temperature { get; set; } = 0.7;

    public string[] AvailableModels { get; set; } = ["qwen2.5-coder:14b"];
}
