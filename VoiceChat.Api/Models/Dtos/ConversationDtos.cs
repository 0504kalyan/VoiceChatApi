namespace VoiceChat.Api.Models.Dtos;

public record ConversationListItemDto(Guid Id, string? Title, string Model, DateTimeOffset UpdatedAt);

public record CreateConversationRequest(string? Title, string? Model);

/// <summary>Partial update for a conversation (e.g. rename or switch Gemini model for subsequent replies).</summary>
public record UpdateConversationRequest(string? Model, string? Title);

public record MessageDto(
    Guid Id,
    string Role,
    string Content,
    string InputMode,
    DateTimeOffset CreatedAt,
    bool IsGenerationComplete);

/// <summary>Stored assistant reply paired with the user request (text + JSON snapshot).</summary>
public record ResponseArchiveDto(Guid Id, string UserRequest, string ResponseText, string ResponseJson, DateTimeOffset CreatedAt);
