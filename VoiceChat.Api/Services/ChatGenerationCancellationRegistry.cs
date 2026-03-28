using System.Collections.Concurrent;

namespace VoiceChat.Api.Services;

/// <summary>
/// Per-conversation cancellation for in-flight assistant generation (Stop button).
/// Keyed by conversation only so Stop works after SignalR reconnect (connection id changes).
/// </summary>
public sealed class ChatGenerationCancellationRegistry
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _sources = new();

    private static string Key(Guid conversationId) => conversationId.ToString("N");

    /// <summary>
    /// Registers a new generation; any previous generation for the same conversation is cancelled.
    /// </summary>
    public CancellationToken Register(Guid conversationId)
    {
        var cts = new CancellationTokenSource();
        _sources.AddOrUpdate(
            Key(conversationId),
            cts,
            (_, old) =>
            {
                try
                {
                    old.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    /* ignore */
                }

                old.Dispose();
                return cts;
            });
        return cts.Token;
    }

    public void Cancel(Guid conversationId)
    {
        if (_sources.TryRemove(Key(conversationId), out var cts))
        {
            try
            {
                cts.Cancel();
            }
            finally
            {
                cts.Dispose();
            }
        }
    }

    public void Complete(Guid conversationId)
    {
        if (_sources.TryRemove(Key(conversationId), out var cts))
            cts.Dispose();
    }
}
