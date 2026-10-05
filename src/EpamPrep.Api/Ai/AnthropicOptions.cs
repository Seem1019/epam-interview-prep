namespace EpamPrep.Api.Ai;

public sealed class AnthropicOptions
{
    public const string Section = "Anthropic";

    /// <summary>
    /// "cli": local Claude Code (`claude -p`) with your Claude.ai login — uses your subscription, no API key.
    /// "api": Anthropic API with ANTHROPIC_API_KEY (pay per use).
    /// </summary>
    public string Provider { get; set; } = "cli";

    public CliOptions Cli { get; set; } = new();

    /// <summary>Resolved from ANTHROPIC_API_KEY or user-secrets "Anthropic:ApiKey". Never stored in appsettings.json.</summary>
    public string? ApiKey { get; set; }

    public string Model { get; set; } = "claude-opus-5-5";

    /// <summary>Server-side refusal fallback (beta server-side-fallback-2026-07-01, fallbacks: "default").</summary>
    public bool UseServerSideFallback { get; set; } = true;

    /// <summary>Canned responses without calling the API. Only for UI smoke tests.</summary>
    public bool DemoMode { get; set; }

    public EffortOptions Effort { get; set; } = new();
}

public sealed class CliOptions
{
    /// <summary>Path to the claude executable. Empty = search PATH and ~/.local/bin.</summary>
    public string? Path { get; set; }

    /// <summary>Model alias or ID passed to --model ("sonnet", "haiku", "claude-sonnet-5-5"...).</summary>
    public string Model { get; set; } = "sonnet";

    /// <summary>Optional cheaper/faster model for the interviewer voice only (e.g. "haiku"). Empty = same as Model.</summary>
    public string? InterviewerModel { get; set; }

    public int TimeoutSeconds { get; set; } = 300;
}

public sealed class EffortOptions
{
    public string Interviewer { get; set; } = "low";
    public string Evaluator { get; set; } = "medium";
    public string Report { get; set; } = "high";
    public string Grader { get; set; } = "medium";
    public string Generator { get; set; } = "medium";
    public string Lesson { get; set; } = "medium";
}
