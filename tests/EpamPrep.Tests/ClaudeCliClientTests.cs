using System.Text.Json;
using EpamPrep.Api.Ai;

namespace EpamPrep.Tests;

public class ClaudeCliClientTests
{
    [Fact]
    public void Arguments_IsolateTheSession_AndNeverEnableTools()
    {
        var args = ClaudeCliClient.BuildArguments("sonnet", "low", "sys.md", schema: null, stream: true);

        Assert.Equal("-p", args[0]);
        Assert.Equal("", args[args.IndexOf("--tools") + 1]);          // no Bash/Edit/etc.
        Assert.Equal("", args[args.IndexOf("--setting-sources") + 1]); // personal hooks/plugins don't leak in
        Assert.Contains("--strict-mcp-config", args);
        Assert.Contains("--no-session-persistence", args);
        Assert.Contains("--include-partial-messages", args);
        Assert.Equal("sys.md", args[args.IndexOf("--system-prompt-file") + 1]);
        Assert.DoesNotContain("--json-schema", args);
    }

    [Fact]
    public void Arguments_PassSchema_ForStructuredOutput_AndSanitizeEffort()
    {
        var schema = JsonDocument.Parse("""{"type":"object","properties":{"a":{"type":"string"}}}""").RootElement;

        var args = ClaudeCliClient.BuildArguments("sonnet", "turbo", "sys.md", schema, stream: false);

        Assert.Equal(schema.GetRawText(), args[args.IndexOf("--json-schema") + 1]);
        Assert.Equal("medium", args[args.IndexOf("--effort") + 1]);
        Assert.DoesNotContain("--include-partial-messages", args);
    }

    [Fact]
    public void Prompt_SingleMessage_IsSentAsIs() =>
        Assert.Equal("Grade this.", ClaudeCliClient.RenderPrompt([AiMessage.User("Grade this.")]));

    [Fact]
    public void Prompt_Conversation_IsRenderedAsTranscript_EndingWithTheUserTurn()
    {
        var prompt = ClaudeCliClient.RenderPrompt(
        [
            AiMessage.User("[Controller note] Start."),
            AiMessage.Assistant("What is DI?"),
            AiMessage.User("[Controller note] Go deeper.\n\nCandidate's answer:\nIt injects deps."),
        ]);

        Assert.True(prompt.IndexOf("What is DI?") < prompt.IndexOf("It injects deps."));
        Assert.Contains("[assistant]\nWhat is DI?", prompt.Replace("\r\n", "\n"));
        Assert.EndsWith("without the [assistant] label.", prompt.TrimEnd());
    }
}
