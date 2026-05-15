using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VoiceChat.Api.Data;
using VoiceChat.Api.Interfaces;
using VoiceChat.Api.Models.Dtos;
using VoiceChat.Api.Models.Entities;
using VoiceChat.Api.Options;
using VoiceChat.Api.Services;

namespace VoiceChat.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/ide/workspaces")]
public class IdeWorkspacesController(
    AppDbContext db,
    ILlmClient llm,
    IOptions<GeminiOptions> geminiOptions) : ControllerBase
{
    private static readonly HashSet<string> AllowedActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "explain",
        "fix",
        "refactor",
        "tests"
    };

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<IdeWorkspaceDto>>> List(CancellationToken cancellationToken)
    {
        var userId = User.RequireUserId();
        var rows = await db.IdeWorkspaces
            .AsNoTracking()
            .Where(w => w.UserId == userId)
            .OrderByDescending(w => w.UpdatedAt)
            .Select(w => new IdeWorkspaceDto(w.Id, w.Name, w.CreatedAt, w.UpdatedAt))
            .ToListAsync(cancellationToken);

        return Ok(rows);
    }

    [HttpPost]
    public async Task<ActionResult<IdeWorkspaceDto>> Create(
        [FromBody] CreateIdeWorkspaceRequest? request,
        CancellationToken cancellationToken)
    {
        var userId = User.RequireUserId();
        var now = DateTimeOffset.UtcNow;
        var workspaceName = NormalizeWorkspaceName(request?.Name);
        var workspace = new IdeWorkspace
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = workspaceName,
            NormalizedName = workspaceName.ToUpperInvariant(),
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        };

        workspace.Files.Add(new IdeFile
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Path = "README.md",
            NormalizedPath = NormalizePathForLookup("README.md"),
            Language = "markdown",
            Content = "# ChatAI IDE Workspace\n\nStart editing files from the explorer.",
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        });

        db.IdeWorkspaces.Add(workspace);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(workspace));
    }

    [HttpGet("{id:guid}/tree")]
    public async Task<ActionResult<IReadOnlyList<IdeFileTreeNodeDto>>> Tree(Guid id, CancellationToken cancellationToken)
    {
        var userId = User.RequireUserId();
        if (!await WorkspaceExistsAsync(id, userId, cancellationToken))
            return NotFound();

        var files = await db.IdeFiles
            .AsNoTracking()
            .Where(f => f.WorkspaceId == id)
            .OrderBy(f => f.Path)
            .Select(f => new { f.Path, f.Language })
            .ToListAsync(cancellationToken);

        return Ok(BuildTree(files.Select(f => (f.Path, f.Language))));
    }

    [HttpGet("{id:guid}/files")]
    public async Task<ActionResult<IdeFileDto>> ReadFile(
        Guid id,
        [FromQuery] string path,
        CancellationToken cancellationToken)
    {
        var userId = User.RequireUserId();
        var normalizedPath = NormalizePath(path);
        if (normalizedPath is null)
            return BadRequest(new { message = "A valid file path is required." });

        var file = await db.IdeFiles
            .AsNoTracking()
            .Include(f => f.Workspace)
            .FirstOrDefaultAsync(
                f => f.WorkspaceId == id && f.NormalizedPath == NormalizePathForLookup(normalizedPath) && f.UserId == userId,
                cancellationToken);

        if (file is null)
            return NotFound();

        return Ok(ToDto(file));
    }

    [HttpPut("{id:guid}/files")]
    public async Task<ActionResult<IdeFileDto>> SaveFile(
        Guid id,
        [FromQuery] string path,
        [FromBody] SaveIdeFileRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.RequireUserId();
        var normalizedPath = NormalizePath(path);
        if (normalizedPath is null)
            return BadRequest(new { message = "A valid file path is required." });

        var workspace = await db.IdeWorkspaces.FirstOrDefaultAsync(
            w => w.Id == id && w.UserId == userId,
            cancellationToken);
        if (workspace is null)
            return NotFound();

        var now = DateTimeOffset.UtcNow;
        var normalizedPathForLookup = NormalizePathForLookup(normalizedPath);
        var file = await db.IdeFiles.FirstOrDefaultAsync(
            f => f.WorkspaceId == id && f.NormalizedPath == normalizedPathForLookup && f.UserId == userId,
            cancellationToken);

        if (file is null)
        {
            file = new IdeFile
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                WorkspaceId = id,
                Path = normalizedPath,
                NormalizedPath = normalizedPathForLookup,
                CreatedAt = now,
                IsActive = true
            };
            db.IdeFiles.Add(file);
        }

        file.Content = request.Content;
        file.Language = ResolveLanguage(normalizedPath, request.Language);
        file.UpdatedAt = now;
        workspace.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(file));
    }

    [HttpPost("{id:guid}/ai-actions")]
    public async Task<ActionResult<IdeAiActionResponse>> RunAiAction(
        Guid id,
        [FromBody] IdeAiActionRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.RequireUserId();
        if (!await WorkspaceExistsAsync(id, userId, cancellationToken))
            return NotFound();

        if (!AllowedActions.Contains(request.Action))
            return BadRequest(new { message = "Action must be explain, fix, refactor, or tests." });

        var model = ResolveRequestedModel(request.Model);
        var prompt = BuildAiActionPrompt(request);
        var result = await CompleteIdeActionAsync(model, prompt, cancellationToken);

        if (string.IsNullOrWhiteSpace(result))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "ChatAI could not complete this IDE action right now." });

        return Ok(BuildAiResponse(request, result));
    }

    [HttpPost("~/api/ide/ai-actions")]
    public async Task<ActionResult<IdeAiActionResponse>> RunWorkspaceAiAction(
        [FromBody] IdeAiActionRequest request,
        CancellationToken cancellationToken)
    {
        _ = User.RequireUserId();
        if (!AllowedActions.Contains(request.Action) && !string.Equals(request.Action, "generate", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Action must be explain, fix, refactor, tests, or generate." });

        var model = ResolveRequestedModel(request.Model);
        var prompt = BuildAiActionPrompt(request);
        var result = await CompleteIdeActionAsync(model, prompt, cancellationToken);

        if (string.IsNullOrWhiteSpace(result))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "ChatAI could not complete this IDE action right now." });

        return Ok(BuildAiResponse(request, result));
    }

    private async Task<bool> WorkspaceExistsAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken) =>
        await db.IdeWorkspaces.AnyAsync(w => w.Id == workspaceId && w.UserId == userId, cancellationToken);

    private async Task<string?> CompleteIdeActionAsync(
        string model,
        string prompt,
        CancellationToken cancellationToken)
    {
        var messages = new List<(string Role, string Content)> { ("user", prompt) };
        var result = await llm.CompleteChatNonStreamingAsync(model, messages, cancellationToken);
        if (string.IsNullOrWhiteSpace(result))
            return result;

        for (var i = 0; i < 3 && LooksIncompleteFileEdit(result); i++)
        {
            messages.Add(("assistant", result));
            messages.Add(("user",
                "Continue from exactly where you stopped. Do not repeat completed file blocks. " +
                "Finish any incomplete FILE blocks and close all code fences."));

            var continuation = await llm.CompleteChatNonStreamingAsync(model, messages, cancellationToken);
            if (string.IsNullOrWhiteSpace(continuation))
                break;

            result += Environment.NewLine + continuation.Trim();
        }

        return result;
    }

    private static bool LooksIncompleteFileEdit(string text)
    {
        var fenceCount = Regex.Matches(text, "```", RegexOptions.CultureInvariant).Count;
        if (fenceCount % 2 != 0)
            return true;

        var lastFile = text.LastIndexOf("FILE:", StringComparison.OrdinalIgnoreCase);
        if (lastFile < 0)
            return false;

        var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
        return lastFence < lastFile;
    }

    private string ResolveRequestedModel(string? requestedModel)
    {
        var model = requestedModel?.Trim();
        if (string.IsNullOrWhiteSpace(model))
            return LlmRuntime.DefaultChatModel(geminiOptions.Value);

        if (model.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
            model = model["models/".Length..];

        return model;
    }

    private static IdeWorkspaceDto ToDto(IdeWorkspace workspace) =>
        new(workspace.Id, workspace.Name, workspace.CreatedAt, workspace.UpdatedAt);

    private static IdeFileDto ToDto(IdeFile file) =>
        new(file.Id, file.WorkspaceId, file.Path, file.NormalizedPath, file.Language, file.Content, file.CreatedAt, file.UpdatedAt);

    private static string NormalizeWorkspaceName(string? name)
    {
        var normalized = string.Join(' ', (name ?? "My Workspace")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return string.IsNullOrWhiteSpace(normalized) ? "My Workspace" : normalized;
    }

    private static string? NormalizePath(string? path)
    {
        var normalized = path?.Replace('\\', '/').Trim().Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Any(p => p is "." or ".."))
            return null;

        return string.Join('/', parts);
    }

    private static string NormalizePathForLookup(string path) =>
        path.Replace('\\', '/').Trim().Trim('/').ToUpperInvariant();

    private static string ResolveLanguage(string path, string? requestedLanguage)
    {
        if (!string.IsNullOrWhiteSpace(requestedLanguage))
            return requestedLanguage.Trim().ToLowerInvariant();

        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".ts" => "typescript",
            ".js" => "javascript",
            ".html" => "html",
            ".scss" or ".css" => "css",
            ".cs" => "csharp",
            ".json" => "json",
            ".md" => "markdown",
            ".py" => "python",
            _ => "plaintext"
        };
    }

    private static IReadOnlyList<IdeFileTreeNodeDto> BuildTree(IEnumerable<(string Path, string Language)> files)
    {
        var root = new TreeNode(string.Empty, string.Empty, true, null);
        foreach (var file in files)
        {
            var parts = file.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var current = root;
            for (var i = 0; i < parts.Length; i++)
            {
                var isFile = i == parts.Length - 1;
                var childPath = string.Join('/', parts.Take(i + 1));
                if (!current.Children.TryGetValue(parts[i], out var child))
                {
                    child = new TreeNode(parts[i], childPath, !isFile, isFile ? file.Language : null);
                    current.Children[parts[i]] = child;
                }

                current = child;
            }
        }

        return root.Children.Values.Select(ToTreeDto).ToList();
    }

    private static IdeFileTreeNodeDto ToTreeDto(TreeNode node) =>
        new(
            node.Name,
            node.Path,
            node.IsDirectory,
            node.Language,
            node.Children.Values
                .OrderByDescending(c => c.IsDirectory)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(ToTreeDto)
                .ToList());

    private static string BuildAiActionPrompt(IdeAiActionRequest request)
    {
        var isOllama = request.Model?.Trim().StartsWith("ollama:", StringComparison.OrdinalIgnoreCase) == true;
        var maxRelatedFiles = isOllama ? 8 : 20;
        var maxFileContentLength = isOllama ? 4000 : 12000;
        var target = string.IsNullOrWhiteSpace(request.Selection) ? request.Content : request.Selection;
        var builder = new StringBuilder()
            .AppendLine("You are ChatAI inside a Cursor-like IDE. Return practical, concise coding help.")
            .AppendLine("When changing code, return concrete file edits only for relevant files.")
            .AppendLine("Inspect all provided related files and modify every file needed for the user's request, even when the user does not mention file names.")
            .AppendLine("Prefer updating existing files. Do not create new files/assets unless the user explicitly asks for new components, documentation, tests, or no existing file can correctly hold the change.")
            .AppendLine("When creating files is required, use normal project structure, correct file extensions, and consistent naming.")
            .AppendLine("For every file edit, use this exact format:")
            .AppendLine("FILE: relative/path.ext")
            .AppendLine("```language")
            .AppendLine("full replacement content for that file")
            .AppendLine("```")
            .AppendLine("Do not return unrelated files. Do not edit migrations or generated build artifacts unless the user specifically asks.")
            .AppendLine($"Action: {request.Action}")
            .AppendLine($"Requested model: {request.Model ?? "server-default"}")
            .AppendLine($"File: {request.Path ?? "untitled"}")
            .AppendLine($"Language: {request.Language ?? "plaintext"}");

        if (isOllama)
        {
            builder
                .AppendLine("Local Ollama mode: be direct and concise. Use the provided indexed files first and avoid repeating large unchanged code.")
                .AppendLine("If the request asks to analyze the project, summarize the most important findings and mention which files should be changed before proposing edits.");
        }

        if (!string.IsNullOrWhiteSpace(request.Instruction))
            builder.AppendLine($"User instruction: {request.Instruction}");

        builder.AppendLine();
        if (!string.IsNullOrWhiteSpace(target))
        {
            builder
                .AppendLine("Active code:")
                .AppendLine("```")
                .AppendLine(target)
                .AppendLine("```");
        }
        else
        {
            builder.AppendLine("No active file content was selected. Treat this as a workspace-level request and use the related workspace files below.");
        }

        if (request.Files is { Count: > 0 })
        {
            builder.AppendLine().AppendLine("Related workspace files:");
            foreach (var file in request.Files.Take(maxRelatedFiles))
            {
                var content = file.Content.Length > maxFileContentLength
                    ? file.Content[..maxFileContentLength]
                    : file.Content;
                builder
                    .AppendLine($"File: {file.Path} ({file.Language})")
                    .AppendLine("```")
                    .AppendLine(content)
                    .AppendLine("```");
            }
        }

        return builder.ToString();
    }

    private static IdeAiActionResponse BuildAiResponse(IdeAiActionRequest request, string result)
    {
        var trimmed = result.Trim();
        var edits = ExtractFileEdits(trimmed);
        if (edits.Count == 0 && !string.Equals(request.Action, "explain", StringComparison.OrdinalIgnoreCase))
        {
            var code = ExtractFirstCodeBlock(trimmed);
            if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(request.Path))
            {
                edits.Add(new IdeProposedFileEditDto(
                    request.Path!,
                    request.Language ?? "plaintext",
                    code,
                    $"Proposed {request.Action} change from ChatAI."));
            }
        }

        return new IdeAiActionResponse(trimmed, edits);
    }

    private static List<IdeProposedFileEditDto> ExtractFileEdits(string text)
    {
        var edits = new List<IdeProposedFileEditDto>();
        var matches = Regex.Matches(
            text,
            @"FILE:\s*(?<path>[^\r\n]+)\s*```(?<language>[^\r\n`]*)\r?\n(?<content>[\s\S]*?)```",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        foreach (Match match in matches)
        {
            var path = match.Groups["path"].Value.Trim().Trim('`', '"', '\'');
            var content = match.Groups["content"].Value.Trim();
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(content))
                continue;

            edits.Add(new IdeProposedFileEditDto(
                path.Replace('\\', '/'),
                NormalizeLanguage(match.Groups["language"].Value),
                content,
                "Applied from ChatAI file edit block."));
        }

        return edits;
    }

    private static string NormalizeLanguage(string language)
    {
        var normalized = language.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? "plaintext" : normalized;
    }

    private static string? ExtractFirstCodeBlock(string text)
    {
        var start = text.IndexOf("```", StringComparison.Ordinal);
        if (start < 0)
            return null;

        var contentStart = text.IndexOf('\n', start + 3);
        if (contentStart < 0)
            return null;

        var end = text.IndexOf("```", contentStart + 1, StringComparison.Ordinal);
        return end < 0 ? null : text[(contentStart + 1)..end].Trim();
    }

    private sealed class TreeNode(string name, string path, bool isDirectory, string? language)
    {
        public string Name { get; } = name;
        public string Path { get; } = path;
        public bool IsDirectory { get; } = isDirectory;
        public string? Language { get; } = language;
        public SortedDictionary<string, TreeNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
