using System.Text.Json;
using EpamPrep.Api.Ai;
using EpamPrep.Api.Data;
using EpamPrep.Api.Features.Interview;
using EpamPrep.Api.Features.Pronunciation;
using EpamPrep.Api.Features.Quiz;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EpamPrep.Tests;

/// <summary>End-to-end service flows against real SQLite + real prompt files, with a scripted LLM.</summary>
public class ServiceFlowTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly FakeClaudeClient _ai = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 10, 1, 18, 0, 0, TimeSpan.Zero));
    private readonly PromptLibrary _prompts = TestPrompts.Load();

    public void Dispose() => _db.Dispose();

    private const string AssessmentJson = """
        {
          "level_demonstrated": "Intermediate", "score": 6, "justification_es": "Correcto pero sin internals.",
          "decision": "go_deeper", "next_focus": "ask about the state machine", "feedback_es": "Menciona el state machine.",
          "concision": { "verdict": "acceptable", "comment_es": "Bien.", "short_version_en": "" },
          "model_answer_en": "async/await is compiled into a state machine...",
          "misrecognized_terms": [ { "heard": "a sink", "intended": "async" }, { "heard": "skimmer", "intended": "schema registry" } ]
        }
        """;

    [Fact]
    public async Task QuickBlock_OpeningThenAnswer_EvaluatesAndAsksDeeperFollowUp()
    {
        var pronunciation = new PronunciationService(_db.Context, _clock);
        var svc = new InterviewService(_db.Context, _ai, _prompts, pronunciation,
            Options.Create(new InterviewOptions()), Options.Create(new AnthropicOptions()), _clock);
        var events = new List<(string Name, string Json)>();
        Task Emit(string name, object data)
        {
            events.Add((name, JsonSerializer.Serialize(data)));
            return Task.CompletedTask;
        }

        var session = await svc.StartAsync(new StartInterviewRequest("quick", "csharp"), CancellationToken.None);

        // Opening question.
        _ai.TextResponses.Enqueue("Hi! What is async/await in C#?");
        await svc.RunTurnAsync(session.Id, new TurnRequest(null, null, null), Emit, CancellationToken.None);
        Assert.Contains(events, e => e.Name == "delta");
        Assert.Equal("Hi! What is async/await in C#?", (await _db.Context.InterviewTurns.SingleAsync()).Question);

        // Answer → hidden evaluation → follow-up.
        _clock.Advance(TimeSpan.FromMinutes(2));
        events.Clear();
        _ai.JsonResponses.Enqueue(AssessmentJson);
        _ai.TextResponses.Enqueue("Okay. What does the compiler generate?");
        await svc.RunTurnAsync(session.Id, new TurnRequest("It lets you write non blocking code with a sink methods.", 70, "voice"), Emit, CancellationToken.None);

        var assessmentEvent = events.Single(e => e.Name == "assessment");
        Assert.Contains("\"visible\":true", assessmentEvent.Json); // quick mode shows feedback
        Assert.Equal(["status", "assessment", "delta", "done"], events.Select(e => e.Name).Distinct());

        _db.Context.ChangeTracker.Clear();
        var reloaded = await svc.LoadAsync(session.Id, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.Equal(1, reloaded.CurrentDepth); // go_deeper applied
        Assert.Equal(2, reloaded.Turns.Count);
        var answered = reloaded.Turns[0];
        Assert.Equal(6, answered.Score);
        Assert.Equal(11, answered.WordCount);
        Assert.StartsWith("[Controller note] Go one notch deeper", answered.DirectiveAfterAnswer);

        // The interviewer saw the controller directive + the answer, with prompt caching on the last message.
        var interviewerCall = _ai.Calls.Last();
        Assert.True(interviewerCall.Request.CacheLastMessage);
        Assert.Contains("Candidate's answer:", interviewerCall.Request.Messages[^1].Content);
        Assert.All(interviewerCall.Request.System, b => Assert.True(b.CacheBreakpoint));

        // Misrecognized voice terms land in the pronunciation list.
        var terms = await _db.Context.PronunciationTerms.Select(t => t.Term).ToListAsync();
        Assert.Contains("async", terms);
        Assert.Contains("schema registry", terms);
    }

    [Fact]
    public async Task OpenQuizAnswer_IsGradedByAi_AndFailedOnesEnterSpacedRepetition()
    {
        var q = new QuizQuestion
        {
            TopicKey = "aspnet", Level = "Advanced", Type = "open", Prompt = "How would you make POST idempotent?",
            ModelAnswer = "Idempotency-Key...", CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };
        _db.Context.QuizQuestions.Add(q);
        await _db.Context.SaveChangesAsync();

        var svc = new QuizService(_db.Context, _ai, _prompts, Options.Create(new AnthropicOptions()), _clock);
        _ai.JsonResponses.Enqueue("""
            { "score": 45, "verdict": "partial", "level_demonstrated": "Intermediate",
              "justification_es": "Mencionó reintentos pero no la clave de idempotencia.",
              "missing_points_es": ["Idempotency-Key persistida con constraint única"],
              "concision": { "verdict": "concise", "comment_es": "", "short_version_en": "" },
              "model_answer_en": "Use an Idempotency-Key header..." }
            """);

        await svc.AnswerAsync(new QuizAnswerRequest(q.Id, "I would retry carefully.", null, false), CancellationToken.None);

        var attempt = await _db.Context.QuizAttempts.SingleAsync();
        Assert.Equal(45, attempt.Score);
        Assert.False(attempt.Passed);
        var review = await _db.Context.ReviewItems.SingleAsync();
        Assert.Equal(_clock.GetUtcNow().UtcDateTime.AddMinutes(10), review.DueAt);

        // The grader got the question, the reference answer and the candidate's answer with a schema.
        var call = Assert.Single(_ai.Calls);
        Assert.NotNull(call.Schema);
        Assert.Contains("I would retry carefully.", call.Request.Messages[0].Content);
    }

    [Fact]
    public async Task MultipleChoice_IsGradedLocally_WithoutCallingTheAi()
    {
        var q = new QuizQuestion
        {
            TopicKey = "javascript", Level = "Novice", Type = "multiple_choice", Prompt = "?",
            OptionsJson = """["a","b"]""", CorrectOption = 1, CreatedAt = DateTime.UtcNow,
        };
        _db.Context.QuizQuestions.Add(q);
        await _db.Context.SaveChangesAsync();
        var svc = new QuizService(_db.Context, _ai, _prompts, Options.Create(new AnthropicOptions()), _clock);

        await svc.AnswerAsync(new QuizAnswerRequest(q.Id, null, 1, false), CancellationToken.None);

        Assert.Empty(_ai.Calls);
        Assert.True((await _db.Context.QuizAttempts.SingleAsync()).Passed);
        Assert.Empty(_db.Context.ReviewItems); // passed on first try → no review needed
    }
}
