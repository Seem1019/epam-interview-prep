using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace EpamPrep.Api.Ai;

/// <summary>
/// Runs the local Claude Code CLI in headless mode (`claude -p`), authenticated with the user's
/// Claude.ai subscription — no API key, no per-token billing. Personal, local use only.
///
/// Isolation: no tools, no MCP servers, no user/project settings (so personal hooks and plugins
/// never leak into the interviewer), no session files, and our own system prompt replaces Claude Code's.
/// ANTHROPIC_API_KEY is removed from the child environment so the subscription is always used.
/// </summary>
public sealed class ClaudeCliClient : IClaudeClient
{
    private readonly CliOptions _cli;
    private readonly ILogger<ClaudeCliClient> _logger;
    private readonly string? _executable;
    private readonly string _workDir;

    public ClaudeCliClient(IOptions<AnthropicOptions> options, ILogger<ClaudeCliClient> logger)
    {
        _cli = options.Value.Cli;
        _logger = logger;
        _executable = ResolveExecutable(_cli.Path);
        // Empty working directory: no CLAUDE.md or project settings to discover.
        _workDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "epam-prep-claude");
        Directory.CreateDirectory(_workDir);
    }

    public bool IsConfigured => _executable is not null;

    public string? Executable => _executable;

    public async IAsyncEnumerable<string> StreamTextAsync(AiRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Only the interviewer streams, so it may use a lighter model.
        var model = string.IsNullOrWhiteSpace(_cli.InterviewerModel) ? _cli.Model : _cli.InterviewerModel!;
        var channel = Channel.CreateUnbounded<string>();
        var run = RunAsync(request, model, schema: null, onText: t => channel.Writer.TryWrite(t), ct)
            .ContinueWith(t => channel.Writer.TryComplete(t.Exception?.InnerException), TaskScheduler.Default);

        await foreach (var chunk in channel.Reader.ReadAllAsync(ct))
        {
            yield return chunk;
        }
        await run;
        await channel.Reader.Completion; // rethrows a failure from RunAsync
    }

    public async Task<JsonDocument> GetJsonAsync(AiRequest request, JsonElement schema, CancellationToken ct = default)
    {
        var result = await RunAsync(request, _cli.Model, schema, onText: null, ct);

        if (result.TryGetProperty("structured_output", out var structured) && structured.ValueKind == JsonValueKind.Object)
        {
            return JsonDocument.Parse(structured.GetRawText());
        }

        // Fallback: the JSON came back as plain text (possibly inside a ```json fence).
        var text = result.TryGetProperty("result", out var r) ? r.GetString() ?? "" : "";
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            try { return JsonDocument.Parse(text[start..(end + 1)]); }
            catch (JsonException) { /* handled below */ }
        }
        throw new AiException("Claude Code no devolvió el JSON esperado. Intenta de nuevo.");
    }

    /// <summary>Runs one headless call and returns the final "result" event.</summary>
    private async Task<JsonElement> RunAsync(AiRequest request, string model, JsonElement? schema, Action<string>? onText, CancellationToken ct)
    {
        if (_executable is null) throw new AiNotConfiguredException();

        var systemFile = System.IO.Path.Combine(_workDir, $"system-{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(systemFile, string.Join("\n\n---\n\n", request.System.Select(b => b.Text)), ct);

        var psi = new ProcessStartInfo(_executable)
        {
            WorkingDirectory = _workDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false),
        };
        foreach (var arg in BuildArguments(model, request.Effort, systemFile, schema, stream: onText is not null))
        {
            psi.ArgumentList.Add(arg);
        }
        psi.Environment.Remove("ANTHROPIC_API_KEY");
        psi.Environment.Remove("ANTHROPIC_AUTH_TOKEN");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_cli.TimeoutSeconds));

        using var process = new Process { StartInfo = psi };
        var started = Stopwatch.StartNew();
        try
        {
            process.Start();
            // The prompt goes through stdin: no command-line length limit, no quoting issues.
            await process.StandardInput.WriteAsync(RenderPrompt(request.Messages));
            process.StandardInput.Close();

            var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
            JsonElement? result = null;

            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                if (line.Length == 0 || line[0] != '{') continue;
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;

                if (type == "stream_event" && onText is not null && TryGetTextDelta(root, out var text))
                {
                    onText(text);
                }
                else if (type == "result")
                {
                    result = root.Clone();
                }
            }

            await process.WaitForExitAsync(timeout.Token);
            var stderr = await stderrTask;

            if (result is not { } final)
            {
                throw new AiException($"Claude Code terminó sin respuesta (exit {process.ExitCode}). {Trim(stderr)}");
            }

            var isError = final.TryGetProperty("is_error", out var e) && e.GetBoolean();
            var message = final.TryGetProperty("result", out var rr) && rr.ValueKind == JsonValueKind.String ? rr.GetString() ?? "" : "";
            if (isError || (final.TryGetProperty("subtype", out var st) && st.GetString() != "success"))
            {
                if (message.Contains("authenticate", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("401") || message.Contains("login", StringComparison.OrdinalIgnoreCase))
                {
                    throw new AiLoginRequiredException();
                }
                throw new AiException($"Claude Code devolvió un error: {Trim(message)}");
            }

            _logger.LogInformation("claude -p ({Model}, effort {Effort}) en {Ms} ms", model, request.Effort, started.ElapsedMilliseconds);
            return final;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AiException($"Claude Code no respondió en {_cli.TimeoutSeconds} s.");
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            }
            try { File.Delete(systemFile); } catch (IOException) { }
        }
    }

    internal static List<string> BuildArguments(string model, string effort, string systemFile, JsonElement? schema, bool stream)
    {
        var args = new List<string>
        {
            "-p",
            "--model", model,
            "--effort", effort is "low" or "medium" or "high" or "xhigh" or "max" ? effort : "medium",
            "--system-prompt-file", systemFile,
            "--tools", "",
            "--setting-sources", "",
            "--strict-mcp-config",
            "--no-session-persistence",
            "--output-format", "stream-json",
            "--verbose",
        };
        if (stream) args.Add("--include-partial-messages");
        if (schema is { } s)
        {
            args.Add("--json-schema");
            args.Add(s.GetRawText());
        }
        return args;
    }

    /// <summary>
    /// `claude -p` takes one prompt, so a multi-turn chat is rendered as a transcript and the model
    /// is asked for the next assistant turn.
    /// </summary>
    internal static string RenderPrompt(IReadOnlyList<AiMessage> messages)
    {
        if (messages.Count == 1) return messages[0].Content;

        var sb = new StringBuilder();
        sb.AppendLine("Below is the conversation so far. Your previous turns are marked [assistant].");
        sb.AppendLine();
        foreach (var m in messages)
        {
            sb.AppendLine(m.Role == "assistant" ? "[assistant]" : "[user]");
            sb.AppendLine(m.Content.Trim());
            sb.AppendLine();
        }
        sb.AppendLine("Write ONLY your next [assistant] turn, without the [assistant] label.");
        return sb.ToString();
    }

    private static bool TryGetTextDelta(JsonElement root, out string text)
    {
        text = "";
        if (!root.TryGetProperty("event", out var ev)) return false;
        if (!ev.TryGetProperty("type", out var et) || et.GetString() != "content_block_delta") return false;
        if (!ev.TryGetProperty("delta", out var delta)) return false;
        if (!delta.TryGetProperty("type", out var dt) || dt.GetString() != "text_delta") return false;
        text = delta.GetProperty("text").GetString() ?? "";
        return text.Length > 0;
    }

    private static string? ResolveExecutable(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? configured : null;

        var names = OperatingSystem.IsWindows() ? new[] { "claude.exe", "claude.cmd" } : new[] { "claude" };
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Append(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin"));
        return dirs.SelectMany(d => names.Select(n => System.IO.Path.Combine(d, n))).FirstOrDefault(File.Exists);
    }

    private static string Trim(string s) => s.Length > 400 ? s[..400] + "…" : s.Trim();
}

public sealed class AiLoginRequiredException()
    : AiException("Claude Code no tiene sesión iniciada. Abre una terminal, ejecuta `claude`, escribe /login y vuelve a intentar.");
