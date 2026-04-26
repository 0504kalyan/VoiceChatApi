namespace VoiceChat.Api.Models.Dtos;

public sealed record IdeWorkspaceDto(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record CreateIdeWorkspaceRequest(string? Name);

public sealed record IdeFileTreeNodeDto(
    string Name,
    string Path,
    bool IsDirectory,
    string? Language,
    IReadOnlyList<IdeFileTreeNodeDto> Children);

public sealed record IdeFileDto(
    Guid Id,
    Guid WorkspaceId,
    string Path,
    string NormalizedPath,
    string Language,
    string Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SaveIdeFileRequest(string Content, string? Language);

public sealed record IdeAiActionRequest(
    string Action,
    string? Path,
    string? Language,
    string? Selection,
    string? Content,
    string? Instruction);

public sealed record IdeAiActionResponse(string Result);
