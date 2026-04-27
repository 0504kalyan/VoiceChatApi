using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using VoiceChat.Api.Interfaces;
using VoiceChat.Api.Options;

namespace VoiceChat.Api.Services;

public sealed class MultiProviderLlmClient(
    GeminiLlmClient gemini,
    OpenAiCompatibleLlmClient openAiCompatible,
    IOptions<OpenAIOptions> openAiOptions,
    IOptions<DeepSeekOptions> deepSeekOptions,
    IOptions<OllamaOptions> ollamaOptions) : ILlmClient
{
    public IAsyncEnumerable<string> StreamChatAsync(
        string model,
        IReadOnlyList<(string Role, string Content)> messages,
        IReadOnlyList<LlmAttachment>? attachments = null,
        CancellationToken cancellationToken = default)
    {
        var provider = ResolveProvider(model);
        if (provider is null || attachments is { Count: > 0 })
            return gemini.StreamChatAsync(model, messages, attachments, cancellationToken);

        return openAiCompatible.StreamChatAsync(provider, messages, cancellationToken);
    }

    public Task<string?> CompleteChatNonStreamingAsync(
        string model,
        IReadOnlyList<(string Role, string Content)> messages,
        CancellationToken cancellationToken = default)
    {
        var provider = ResolveProvider(model);
        return provider is null
            ? gemini.CompleteChatNonStreamingAsync(model, messages, cancellationToken)
            : openAiCompatible.CompleteChatNonStreamingAsync(provider, messages, cancellationToken);
    }

    private OpenAiCompatibleLlmClient.ProviderConfig? ResolveProvider(string? requestedModel)
    {
        var model = requestedModel?.Trim() ?? string.Empty;
        if (model.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
            model = model["models/".Length..];

        if (model.StartsWith("ollama:", StringComparison.OrdinalIgnoreCase))
        {
            var opts = ollamaOptions.Value;
            var ollamaModel = model["ollama:".Length..].Trim();
            return new OpenAiCompatibleLlmClient.ProviderConfig(
                "Ollama",
                OllamaOptions.SectionName,
                string.Empty,
                opts.BaseUrl,
                string.IsNullOrWhiteSpace(ollamaModel) ? opts.DefaultModel : ollamaModel,
                opts.Temperature,
                RequiresApiKey: false);
        }

        if (IsOpenAiModel(model))
        {
            var opts = openAiOptions.Value;
            return new OpenAiCompatibleLlmClient.ProviderConfig(
                "OpenAI",
                OpenAIOptions.SectionName,
                opts.ApiKey,
                opts.BaseUrl,
                string.IsNullOrWhiteSpace(model) ? opts.DefaultModel : model,
                opts.Temperature);
        }

        if (model.StartsWith("deepseek-", StringComparison.OrdinalIgnoreCase))
        {
            var opts = deepSeekOptions.Value;
            return new OpenAiCompatibleLlmClient.ProviderConfig(
                "DeepSeek",
                DeepSeekOptions.SectionName,
                opts.ApiKey,
                opts.BaseUrl,
                string.IsNullOrWhiteSpace(model) ? opts.DefaultModel : model,
                opts.Temperature);
        }

        return null;
    }

    private static bool IsOpenAiModel(string model) =>
        model.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase)
        || model.StartsWith("o1", StringComparison.OrdinalIgnoreCase)
        || model.StartsWith("o3", StringComparison.OrdinalIgnoreCase)
        || model.StartsWith("o4", StringComparison.OrdinalIgnoreCase);
}
