using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Options;

namespace EpamPrep.Api.Ai;

/// <summary>
/// Adapter over the official Anthropic C# SDK.
/// - Every call streams (long JSON reports never hit HTTP timeouts).
/// - System blocks carry cache breakpoints (guide + rubric + profile are reused across calls).
/// - JSON results use structured outputs (output_config.format) instead of forced tool use,
///   which claude-opus-5-5 no longer accepts.
/// - Server-side refusal fallback ("default") is opt-in via config.
/// </summary>
public sealed class AnthropicClaudeClient : IClaudeClient
{
    private readonly AnthropicOptions _options;
    private readonly ILogger<AnthropicClaudeClient> _logger;
    private readonly AnthropicClient? _client;

    public AnthropicClaudeClient(IOptions<AnthropicOptions> options, ILogger<AnthropicClaudeClient> logger)
    {
        _options = options.Value;
        _logger = logger;
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _client = new AnthropicClient { ApiKey = _options.ApiKey };
        }
    }

    public bool IsConfigured => _client is not null;

    public async IAsyncEnumerable<string> StreamTextAsync(
        AiRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var client = _client ?? throw new AiNotConfiguredException();
        var parameters = Build(request, schema: null);

        await foreach (var ev in client.Beta.Messages.CreateStreaming(parameters, ct))
        {
            if (ev.TryPickContentBlockDelta(out var delta) && delta.Delta.TryPickText(out var text))
            {
                yield return text.Text;
            }
            else if (ev.TryPickDelta(out var messageDelta))
            {
                ThrowOnBadStop(messageDelta.Delta.StopReason?.Raw(), allowMaxTokens: true);
            }
            else if (ev.TryPickStart(out var start))
            {
                LogUsage("stream-start", start.Message.Usage);
            }
        }
    }

    public async Task<JsonDocument> GetJsonAsync(AiRequest request, JsonElement schema, CancellationToken ct = default)
    {
        var client = _client ?? throw new AiNotConfiguredException();
        var parameters = Build(request, schema);
        var buffer = new StringBuilder();

        await foreach (var ev in client.Beta.Messages.CreateStreaming(parameters, ct))
        {
            if (ev.TryPickContentBlockStart(out var blockStart) && blockStart.ContentBlock.TryPickBetaFallback(out _))
            {
                // A server-side fallback restarted the answer on another model: drop the partial output.
                buffer.Clear();
            }
            else if (ev.TryPickContentBlockDelta(out var delta) && delta.Delta.TryPickText(out var text))
            {
                buffer.Append(text.Text);
            }
            else if (ev.TryPickDelta(out var messageDelta))
            {
                ThrowOnBadStop(messageDelta.Delta.StopReason?.Raw(), allowMaxTokens: false);
            }
            else if (ev.TryPickStart(out var start))
            {
                LogUsage("json-start", start.Message.Usage);
            }
        }

        try
        {
            return JsonDocument.Parse(buffer.ToString());
        }
        catch (JsonException ex)
        {
            throw new AiException("La respuesta del modelo no es JSON válido.", ex);
        }
    }

    private MessageCreateParams Build(AiRequest request, JsonElement? schema)
    {
        var system = request.System
            .Select(b => new BetaTextBlockParam
            {
                Text = b.Text,
                CacheControl = b.CacheBreakpoint ? new BetaCacheControlEphemeral() : null,
            })
            .ToList();

        var messages = new List<BetaMessageParam>(request.Messages.Count);
        for (var i = 0; i < request.Messages.Count; i++)
        {
            var m = request.Messages[i];
            var role = m.Role == "assistant" ? Role.Assistant : Role.User;
            var isLast = i == request.Messages.Count - 1;
            if (request.CacheLastMessage && isLast)
            {
                messages.Add(new BetaMessageParam
                {
                    Role = role,
                    Content = new List<BetaContentBlockParam>
                    {
                        new BetaTextBlockParam { Text = m.Content, CacheControl = new BetaCacheControlEphemeral() },
                    },
                });
            }
            else
            {
                messages.Add(new BetaMessageParam { Role = role, Content = m.Content });
            }
        }

        var outputConfig = new BetaOutputConfig { Effort = ParseEffort(request.Effort) };
        if (schema is { } s)
        {
            outputConfig = outputConfig with
            {
                Format = new BetaJsonOutputFormat
                {
                    Schema = s.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()),
                },
            };
        }

        var parameters = new MessageCreateParams
        {
            Model = _options.Model,
            MaxTokens = request.MaxTokens,
            System = system,
            Messages = messages,
            OutputConfig = outputConfig,
        };

        if (_options.UseServerSideFallback)
        {
            parameters = parameters with
            {
                Betas = ["server-side-fallback-2026-07-01"],
                Fallbacks = new BetaFallbacksParam(new Default()),
            };
        }

        return parameters;
    }

    private static Effort ParseEffort(string effort) => effort.ToLowerInvariant() switch
    {
        "low" => Effort.Low,
        "high" => Effort.High,
        "xhigh" => Effort.Xhigh,
        "max" => Effort.Max,
        _ => Effort.Medium,
    };

    private static void ThrowOnBadStop(string? stopReason, bool allowMaxTokens)
    {
        switch (stopReason)
        {
            case "refusal":
                throw new AiRefusalException(null);
            case "max_tokens" when !allowMaxTokens:
                throw new AiException("La respuesta se cortó por max_tokens.");
        }
    }

    private void LogUsage(string phase, BetaUsage usage) =>
        _logger.LogInformation(
            "Claude {Phase}: input={Input} cache_read={CacheRead} cache_write={CacheWrite}",
            phase, usage.InputTokens, usage.CacheReadInputTokens, usage.CacheCreationInputTokens);
}

