namespace VoiceChat.Api.Options;

/// <summary>
/// OpenAI API configuration. Bind with env vars like <c>OpenAI__ApiKey</c>.
/// </summary>
public sealed class OpenAIOptions
{
    public const string SectionName = "OpenAI";

    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";

    public string DefaultModel { get; set; } = "gpt-4o-mini";

    public double? Temperature { get; set; } = 0.7;

    public string[] AvailableModels { get; set; } =
    [
        "gpt-4o-mini",
        "gpt-4o",
        "gpt-4.1-mini"
    ];
}
