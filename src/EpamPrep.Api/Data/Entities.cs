namespace EpamPrep.Api.Data;

public sealed class Profile
{
    public int Id { get; set; } = 1;
    public string CvText { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}

public sealed class InterviewSession
{
    public int Id { get; set; }
    /// <summary>"full" or "quick".</summary>
    public string Mode { get; set; } = "quick";
    /// <summary>Comma-separated competency keys in the order they will be covered.</summary>
    public string Topics { get; set; } = "";
    public int CurrentTopicIndex { get; set; }
    public int CurrentDepth { get; set; }
    /// <summary>Last FlowAction applied (used to avoid clarify loops).</summary>
    public string? LastAction { get; set; }
    public int PlannedMinutes { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime TopicStartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    /// <summary>active | finished.</summary>
    public string Status { get; set; } = "active";
    /// <summary>Controller instruction that opened the conversation (kept verbatim so the prompt prefix stays cacheable).</summary>
    public string OpeningDirective { get; set; } = "";
    public string? ReportJson { get; set; }
    public List<InterviewTurn> Turns { get; set; } = [];

    public IReadOnlyList<string> TopicList => Topics.Split(',', StringSplitOptions.RemoveEmptyEntries);
    public string CurrentTopic => TopicList[Math.Min(CurrentTopicIndex, TopicList.Count - 1)];
}

public sealed class InterviewTurn
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public int Sequence { get; set; }
    public string TopicKey { get; set; } = "";
    public int Depth { get; set; }
    public string Question { get; set; } = "";
    public string? Answer { get; set; }
    public int? AnswerSeconds { get; set; }
    /// <summary>voice | typed.</summary>
    public string? InputMode { get; set; }
    public int? WordCount { get; set; }
    /// <summary>Controller note sent to the interviewer together with this answer (replayed verbatim).</summary>
    public string? DirectiveAfterAnswer { get; set; }
    public string? AssessmentJson { get; set; }
    public int? Score { get; set; }
    public string? Level { get; set; }
    public DateTime AskedAt { get; set; }
    public DateTime? AnsweredAt { get; set; }
}

public sealed class QuizQuestion
{
    public int Id { get; set; }
    public string TopicKey { get; set; } = "";
    public string? Subtopic { get; set; }
    /// <summary>Novice | Intermediate | Advanced | Expert.</summary>
    public string Level { get; set; } = "Intermediate";
    /// <summary>multiple_choice | output | open | code_review.</summary>
    public string Type { get; set; } = "multiple_choice";
    public string Prompt { get; set; } = "";
    public string? Code { get; set; }
    public string? CodeLang { get; set; }
    /// <summary>JSON array of options (multiple choice / output).</summary>
    public string? OptionsJson { get; set; }
    public int? CorrectOption { get; set; }
    public string? ExpectedOutput { get; set; }
    public string? ModelAnswer { get; set; }
    public string? ExplanationEs { get; set; }
    /// <summary>seed | ai.</summary>
    public string Source { get; set; } = "seed";
    public DateTime CreatedAt { get; set; }
}

public sealed class QuizAttempt
{
    public int Id { get; set; }
    public int QuestionId { get; set; }
    public QuizQuestion? Question { get; set; }
    public string TopicKey { get; set; } = "";
    public string Level { get; set; } = "";
    public string Answer { get; set; } = "";
    public int Score { get; set; }
    public bool Passed { get; set; }
    public string? FeedbackJson { get; set; }
    public bool WasReview { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Spaced-repetition state for a question the user failed at least once.</summary>
public sealed class ReviewItem
{
    public int Id { get; set; }
    public int QuestionId { get; set; }
    public QuizQuestion? Question { get; set; }
    public double EaseFactor { get; set; } = 2.5;
    public double IntervalMinutes { get; set; }
    public int Repetitions { get; set; }
    public DateTime DueAt { get; set; }
    public DateTime? LastReviewedAt { get; set; }
    public int Lapses { get; set; }
}

public sealed class Lesson
{
    public int Id { get; set; }
    public string TopicKey { get; set; } = "";
    public string SubtopicKey { get; set; } = "";
    public string ContentJson { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public sealed class PronunciationTerm
{
    public int Id { get; set; }
    public string Term { get; set; } = "";
    public string Respelling { get; set; } = "";
    public string? Ipa { get; set; }
    public string? TipEs { get; set; }
    /// <summary>Pipe-separated transcriptions accepted as correct (homophones included: cache|cash).</summary>
    public string Accepted { get; set; } = "";
    /// <summary>seed | lesson | simulator | manual.</summary>
    public string Source { get; set; } = "seed";
    public int Attempts { get; set; }
    public int Successes { get; set; }
    public string? LastHeard { get; set; }
    public DateTime? LastPracticedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class PlanTaskState
{
    /// <summary>Stable key: yyyy-MM-dd|kind|topic.</summary>
    public string Key { get; set; } = "";
    public bool Done { get; set; }
    public DateTime UpdatedAt { get; set; }
}
