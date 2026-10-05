using System.Text.Json;

namespace EpamPrep.Api.Ai;

/// <summary>
/// Thin port over the LLM. The rest of the app depends on this interface only,
/// so tests can plug a fake and the SDK stays an implementation detail (adapter pattern).
/// </summary>
public interface IClaudeClient
{
    bool IsConfigured { get; }

    /// <summary>Streams plain text deltas (used for the interviewer voice).</summary>
    IAsyncEnumerable<string> StreamTextAsync(AiRequest request, CancellationToken ct = default);

    /// <summary>Returns a JSON document constrained by <paramref name="schema"/> (structured outputs).</summary>
    Task<JsonDocument> GetJsonAsync(AiRequest request, JsonElement schema, CancellationToken ct = default);
}

/// <param name="System">System blocks in order. Stable content first so prompt caching can reuse the prefix.</param>
/// <param name="Messages">Conversation turns (role "user" or "assistant").</param>
/// <param name="Effort">low | medium | high | xhigh | max.</param>
/// <param name="CacheLastMessage">Adds a cache breakpoint on the last message (incremental caching of long chats).</param>
public sealed record AiRequest(
    IReadOnlyList<AiSystemBlock> System,
    IReadOnlyList<AiMessage> Messages,
    string Effort = "medium",
    int MaxTokens = 16000,
    bool CacheLastMessage = false);

public sealed record AiSystemBlock(string Text, bool CacheBreakpoint = false);

public sealed record AiMessage(string Role, string Content)
{
    public static AiMessage User(string content) => new("user", content);
    public static AiMessage Assistant(string content) => new("assistant", content);
}

public class AiException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class AiNotConfiguredException()
    : AiException("ANTHROPIC_API_KEY no está configurada.");

public sealed class AiRefusalException(string? category)
    : AiException($"El modelo rechazó la solicitud (categoría: {category ?? "desconocida"}).");
