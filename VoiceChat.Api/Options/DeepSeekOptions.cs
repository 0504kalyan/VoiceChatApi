namespace VoiceChat.Api.Options;

/// <summary>
/// DeepSeek API configuration. Bind with env vars like <c>DeepSeek__ApiKey</c>.
/// </summary>
public sealed class DeepSeekOptions
{
    public const string SectionName = "DeepSeek";

    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://api.deepseek.com/v1/";

    public string DefaultModel { get; set; } = "deepseek-chat";

    public double? Temperature { get; set; } = 0.7;

    public string[] AvailableModels { get; set; } =
    [
        "deepseek-chat",
        "deepseek-coder",
        "deepseek-reasoner"
    ];
}
