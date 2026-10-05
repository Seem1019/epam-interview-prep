using System.Globalization;
using System.Text;
using System.Text.Json;
using EpamPrep.Api.Ai;
using EpamPrep.Api.Data;
using EpamPrep.Api.Domain;
using EpamPrep.Api.Features.Pronunciation;
using EpamPrep.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EpamPrep.Api.Features.Interview;

public sealed record StartInterviewRequest(string Mode, string? TopicKey);

public sealed record TurnRequest(string? Answer, int? Seconds, string? InputMode);

public sealed class InterviewOptions
{
    public DateTime Date { get; set; } = new(2026, 10, 5, 9, 0, 0);
    public int FullModeMinutes { get; set; } = 120;
    public int QuickModeMinutes { get; set; } = 18;
    public int MaxDepthPerTopic { get; set; } = 6;
}

/// <summary>Callback used to push events to the client (SSE in production, a list in tests).</summary>
public delegate Task EmitAsync(string eventName, object data);

public sealed class InterviewService(
    AppDbContext db,
    IClaudeClient ai,
    PromptLibrary prompts,
    PronunciationService pronunciation,
    Microsoft.Extensions.Options.IOptions<InterviewOptions> interviewOptions,
    Microsoft.Extensions.Options.IOptions<AnthropicOptions> aiOptions,
    TimeProvider clock)
{
    private readonly InterviewOptions _opt = interviewOptions.Value;
    private readonly EffortOptions _effort = aiOptions.Value.Effort;

    public async Task<InterviewSession> StartAsync(StartInterviewRequest request, CancellationToken ct)
    {
        var quick = request.Mode != "full";
        IReadOnlyList<string> topics;
        if (quick)
        {
            var competency = Catalog.Find(request.TopicKey)
                ?? throw new ArgumentException("Elige un tema válido para el bloque rápido.");
            topics = [competency.Key];
        }
        else
        {
            topics = Catalog.FullInterviewOrder;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var planned = quick ? _opt.QuickModeMinutes : _opt.FullModeMinutes;
        var session = new InterviewSession
        {
            Mode = quick ? "quick" : "full",
            Topics = string.Join(',', topics),
            PlannedMinutes = planned,
            StartedAt = now,
            TopicStartedAt = now,
            OpeningDirective = OpeningDirective(quick, topics, planned),
        };
        db.InterviewSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session;
    }

    /// <summary>
    /// One interview step: store the answer (if any) → hidden evaluation → controller decision →
    /// stream the interviewer's next question. Idempotent on retry: steps already persisted are skipped.
    /// </summary>
    public async Task RunTurnAsync(int sessionId, TurnRequest request, EmitAsync emit, CancellationToken ct)
    {
        var session = await LoadAsync(sessionId, ct) ?? throw new KeyNotFoundException("Sesión no encontrada.");
        if (session.Status != "active" || session.EndedAt is not null)
        {
            throw new InvalidOperationException("La entrevista ya terminó. Genera el reporte.");
        }

        var profile = await ProfileText(ct);
        var quick = session.Mode == "quick";
        var last = session.Turns.LastOrDefault();
        var ended = false;

        if (last is not null)
        {
            if (last.Answer is null)
            {
                if (string.IsNullOrWhiteSpace(request.Answer))
                {
                    throw new ArgumentException("La respuesta está vacía.");
                }
                last.Answer = request.Answer.Trim();
                last.AnswerSeconds = request.Seconds;
                last.InputMode = request.InputMode == "voice" ? "voice" : "typed";
                last.WordCount = TextStats.WordCount(last.Answer);
                last.AnsweredAt = clock.GetUtcNow().UtcDateTime;
                await db.SaveChangesAsync(ct);
            }

            if (last.AssessmentJson is null)
            {
                await emit("status", new { phase = "evaluating" });
                var assessmentDoc = await EvaluateAsync(session, last, profile, ct);
                var assessment = AiResultParser.ParseAssessment(assessmentDoc.RootElement);
                last.AssessmentJson = assessmentDoc.RootElement.GetRawText();
                last.Score = assessment.Score;
                last.Level = assessment.LevelDemonstrated.ToString();

                var previousTopic = session.CurrentTopic;
                var step = ApplyFlow(session, assessment);
                last.DirectiveAfterAnswer = AfterAnswerDirective(session, step, assessment, (clock.GetUtcNow().UtcDateTime - session.StartedAt).TotalMinutes);
                await db.SaveChangesAsync(ct);

                if (last.InputMode == "voice" && assessment.MisrecognizedTerms.Count > 0)
                {
                    await pronunciation.AddFromSimulatorAsync(assessment.MisrecognizedTerms, ct);
                }

                // Quick mode shows coaching right away; full mode keeps it for the final report.
                await emit("assessment", new { turnId = last.Id, visible = quick, assessment = quick ? assessment : null });

                if (session.CurrentTopic != previousTopic)
                {
                    await emit("topic", TopicInfo(session));
                }
            }

            ended = session.EndedAt is not null;
        }
        else
        {
            await emit("topic", TopicInfo(session));
        }

        await emit("status", new { phase = "asking" });
        var text = new StringBuilder();
        var request2 = new AiRequest(
            prompts.SystemFor("interviewer", profile),
            BuildConversation(session),
            Effort: _effort.Interviewer,
            MaxTokens: 4000,
            CacheLastMessage: true);

        await foreach (var delta in ai.StreamTextAsync(request2, ct))
        {
            text.Append(delta);
            await emit("delta", new { text = delta });
        }

        int? newTurnId = null;
        if (!ended)
        {
            var turn = new InterviewTurn
            {
                SessionId = session.Id,
                Sequence = session.Turns.Count,
                TopicKey = session.CurrentTopic,
                Depth = session.CurrentDepth,
                Question = text.ToString().Trim(),
                AskedAt = clock.GetUtcNow().UtcDateTime,
            };
            db.InterviewTurns.Add(turn);
            await db.SaveChangesAsync(ct);
            newTurnId = turn.Id;
        }

        await emit("done", new { turnId = newTurnId, ended });
    }

    public async Task<JsonElement> FinishAsync(int sessionId, CancellationToken ct)
    {
        var session = await LoadAsync(sessionId, ct) ?? throw new KeyNotFoundException("Sesión no encontrada.");
        var answered = session.Turns.Where(t => t.Answer is not null && t.AssessmentJson is not null).ToList();
        if (answered.Count == 0)
        {
            throw new InvalidOperationException("No hay respuestas evaluadas todavía; responde al menos una pregunta.");
        }

        session.EndedAt ??= clock.GetUtcNow().UtcDateTime;
        var profile = await ProfileText(ct);
        var reportDoc = await ai.GetJsonAsync(
            new AiRequest(
                prompts.SystemFor("report", profile),
                [AiMessage.User(ReportInput(session, answered))],
                Effort: _effort.Report,
                MaxTokens: 32000),
            prompts.Schema("report"),
            ct);

        var report = BuildReport(session, answered, reportDoc.RootElement, clock.GetUtcNow().UtcDateTime);
        session.ReportJson = report.GetRawText();
        session.Status = "finished";
        session.EndedAt ??= clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return report;
    }

    public Task<InterviewSession?> LoadAsync(int id, CancellationToken ct) =>
        db.InterviewSessions
            .Include(s => s.Turns.OrderBy(t => t.Sequence))
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    // ---------- controller ----------

    private FlowStep ApplyFlow(InterviewSession session, AnswerAssessment assessment)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var topics = session.TopicList;
        var weights = topics.Select(k => Catalog.Get(k).InterviewWeight).ToList();
        var context = new FlowContext(
            IsQuick: session.Mode == "quick",
            TopicIndex: session.CurrentTopicIndex,
            TopicCount: topics.Count,
            Depth: session.CurrentDepth,
            TopicMinutes: (now - session.TopicStartedAt).TotalMinutes,
            TopicBudgetMinutes: InterviewFlow.TopicBudget(session.PlannedMinutes, weights, session.CurrentTopicIndex),
            TotalMinutes: (now - session.StartedAt).TotalMinutes,
            PlannedMinutes: session.PlannedMinutes,
            MaxDepth: _opt.MaxDepthPerTopic,
            LastDecisionWasClarify: session.LastAction == nameof(FlowAction.Clarify));

        var step = InterviewFlow.Decide(context, assessment);
        switch (step.Action)
        {
            case FlowAction.GoDeeper:
                session.CurrentDepth++;
                break;
            case FlowAction.Clarify:
                break;
            case FlowAction.NextTopic:
                session.CurrentTopicIndex++;
                session.CurrentDepth = 0;
                session.TopicStartedAt = now;
                break;
            case FlowAction.NewThread:
                session.CurrentDepth = 0;
                session.TopicStartedAt = now;
                break;
            case FlowAction.End:
                session.EndedAt = now;
                break;
        }
        session.LastAction = step.Action.ToString();
        return step;
    }

    private static object TopicInfo(InterviewSession s)
    {
        var c = Catalog.Get(s.CurrentTopic);
        return new { key = c.Key, title = c.Title, index = s.CurrentTopicIndex, total = s.TopicList.Count };
    }

    // ---------- prompts (user-message side; system prompts live in prompts/) ----------

    internal static string OpeningDirective(bool quick, IReadOnlyList<string> topics, int plannedMinutes)
    {
        if (quick)
        {
            var c = Catalog.Get(topics[0]);
            return $"""
                [Controller note] Mode: QUICK BLOCK on "{c.Title}" (~{plannedMinutes} minutes). The app shows the candidate feedback after every answer, so you never give feedback yourself.
                Subtopics you can draw from: {string.Join("; ", c.Subtopics.Select(s => s.Title))}.
                Start now: one-sentence greeting, then a basic opening question on {c.Title}.
                """;
        }

        var list = string.Join(", ", topics.Select((k, i) => $"{i + 1}. {Catalog.Get(k).Title}"));
        return $"""
            [Controller note] Mode: FULL MOCK INTERVIEW (~{plannedMinutes} minutes, no feedback until the final report).
            Topics in order: {list}.
            Start now: greet the candidate in one sentence, explain the format in one sentence (we start from basics and go deeper on each topic), then open topic 1/{topics.Count} "{Catalog.Get(topics[0]).Title}" with a basic question.
            """;
    }

    internal static string AfterAnswerDirective(InterviewSession s, FlowStep step, AnswerAssessment a, double elapsedMinutes)
    {
        var time = $"Elapsed: {elapsedMinutes:0} of ~{s.PlannedMinutes} min.";
        var topic = Catalog.Get(s.CurrentTopic);
        var focus = string.IsNullOrWhiteSpace(a.NextFocus) || a.NextFocus == "-" ? null : a.NextFocus;
        var codeTopic = topic.Key is not ("process" or "leadership" or "genai");

        var instruction = step.Action switch
        {
            FlowAction.GoDeeper =>
                $"Go one notch deeper on the same thread (follow-up #{s.CurrentDepth})." +
                (focus is null ? "" : $" Probe: {focus}") +
                (codeTopic && s.CurrentDepth == 2 ? " If you have not used a code snippet in this topic yet, use a predict-the-output or code-review snippet now." : ""),
            FlowAction.Clarify =>
                "The answer was too vague to grade. Ask for specifics or a concrete example, without hinting at the answer." +
                (focus is null ? "" : $" Probe: {focus}"),
            FlowAction.NextTopic =>
                $"This thread is finished. Neutral one-line transition, then open topic {s.CurrentTopicIndex + 1}/{s.TopicList.Count} \"{topic.Title}\" with a basic question." +
                (focus is null ? "" : $" Suggested opening area: {focus}"),
            FlowAction.NewThread =>
                $"This thread is finished. Stay on \"{topic.Title}\" but open a different subtopic not yet covered in this conversation, starting again with a basic question. Options: {string.Join("; ", topic.Subtopics.Select(x => x.Title))}.",
            FlowAction.End =>
                "The interview is over. Thank the candidate in one or two sentences and stop. Do not ask anything else.",
            _ => "Continue.",
        };

        return $"[Controller note] {instruction} {time}";
    }

    internal static List<AiMessage> BuildConversation(InterviewSession session)
    {
        var messages = new List<AiMessage> { AiMessage.User(session.OpeningDirective) };
        foreach (var turn in session.Turns.OrderBy(t => t.Sequence))
        {
            messages.Add(AiMessage.Assistant(turn.Question));
            if (turn.Answer is not null && turn.DirectiveAfterAnswer is not null)
            {
                messages.Add(AiMessage.User($"{turn.DirectiveAfterAnswer}\n\nCandidate's answer:\n{turn.Answer}"));
            }
        }
        return messages;
    }

    private async Task<JsonDocument> EvaluateAsync(InterviewSession s, InterviewTurn last, string? profile, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var topic = Catalog.Get(last.TopicKey);
        var thread = s.Turns
            .Where(t => t.TopicKey == last.TopicKey && t.Id != last.Id && t.Answer is not null)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"Mode: {s.Mode}");
        sb.AppendLine($"Competency: {topic.Title} ({topic.Key}); previous EPAM level: {topic.Previous}; target: {topic.Target}");
        sb.AppendLine($"Depth of the current thread: {last.Depth} (0 = opening basic question); max depth {_opt.MaxDepthPerTopic}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Time on this topic: {(now - s.TopicStartedAt).TotalMinutes:0.0} min; total: {(now - s.StartedAt).TotalMinutes:0.0} of {s.PlannedMinutes} min");
        sb.AppendLine($"Previous decision: {s.LastAction ?? "none"}");
        sb.AppendLine();
        if (thread.Count > 0)
        {
            sb.AppendLine("## Earlier questions and answers in this competency");
            foreach (var t in thread)
            {
                sb.AppendLine($"Q (depth {t.Depth}): {t.Question}");
                sb.AppendLine($"A: {t.Answer}");
                sb.AppendLine($"Assessed: {t.Level}, score {t.Score}/10");
                sb.AppendLine();
            }
        }
        sb.AppendLine("## Question just asked");
        sb.AppendLine(last.Question);
        sb.AppendLine();
        sb.AppendLine($"## Candidate's answer ({(last.InputMode == "voice" ? "voice transcript" : "typed")}, {last.WordCount} words, {last.AnswerSeconds?.ToString() ?? "?"} seconds)");
        sb.AppendLine(last.Answer);

        return await ai.GetJsonAsync(
            new AiRequest(prompts.SystemFor("evaluator", profile), [AiMessage.User(sb.ToString())], Effort: _effort.Evaluator, MaxTokens: 8000),
            prompts.Schema("assessment"),
            ct);
    }

    private static string ReportInput(InterviewSession s, IReadOnlyList<InterviewTurn> answered)
    {
        var sb = new StringBuilder();
        var minutes = ((s.EndedAt ?? DateTime.UtcNow) - s.StartedAt).TotalMinutes;
        sb.AppendLine(CultureInfo.InvariantCulture, $"Mode: {s.Mode}. Duration: {minutes:0} minutes. Answers: {answered.Count}.");
        var withSeconds = answered.Where(t => t.AnswerSeconds is > 0).ToList();
        var avgSeconds = withSeconds.Count > 0
            ? FormattableString.Invariant($", {withSeconds.Average(t => t.AnswerSeconds!.Value):0} seconds")
            : "";
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"Average answer: {answered.Average(t => t.WordCount ?? 0):0} words{avgSeconds}. Answers over 150 words: {answered.Count(t => t.WordCount > 150)}.");
        sb.AppendLine();

        foreach (var group in answered.GroupBy(t => t.TopicKey))
        {
            var c = Catalog.Get(group.Key);
            sb.AppendLine($"## {c.Title} ({c.Key}) — previous EPAM level: {c.Previous}, target: {c.Target}");
            foreach (var t in group)
            {
                var a = AiResultParser.ParseAssessment(JsonDocument.Parse(t.AssessmentJson!).RootElement);
                sb.AppendLine($"Q (depth {t.Depth}): {t.Question}");
                sb.AppendLine($"A ({t.InputMode}, {t.WordCount} words, {t.AnswerSeconds?.ToString() ?? "?"} s): {t.Answer}");
                sb.AppendLine($"Evaluator: {a.LevelDemonstrated}, {a.Score}/10, concision {a.Concision.Verdict}. {a.JustificationEs}");
                if (a.MisrecognizedTerms.Count > 0)
                {
                    sb.AppendLine($"Likely mispronounced: {string.Join(", ", a.MisrecognizedTerms.Select(m => $"\"{m.Heard}\" → {m.Intended}"))}");
                }
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }

    internal const int FailedScoreThreshold = 5;

    /// <summary>Merges the model's report with deterministic data (previous levels, failed questions).</summary>
    internal static JsonElement BuildReport(InterviewSession s, IReadOnlyList<InterviewTurn> answered, JsonElement ai, DateTime now)
    {
        var failed = answered
            .Where(t => t.Score <= FailedScoreThreshold)
            .Select(t =>
            {
                var a = AiResultParser.ParseAssessment(JsonDocument.Parse(t.AssessmentJson!).RootElement);
                return new
                {
                    turnId = t.Id,
                    topicKey = t.TopicKey,
                    topicTitle = Catalog.Get(t.TopicKey).Title,
                    question = t.Question,
                    answer = t.Answer,
                    score = t.Score,
                    level = t.Level,
                    justificationEs = a.JustificationEs,
                    feedbackEs = a.FeedbackEs,
                    modelAnswerEn = a.ModelAnswerEn,
                };
            })
            .ToList();

        var previous = Catalog.All.ToDictionary(c => c.Key, c => new { previous = c.Previous.ToString(), target = c.Target.ToString(), title = c.Title });

        var report = new
        {
            generatedAt = now,
            mode = s.Mode,
            minutes = Math.Round(((s.EndedAt ?? now) - s.StartedAt).TotalMinutes),
            answers = answered.Count,
            averageWords = Math.Round(answered.Average(t => t.WordCount ?? 0)),
            tooLong = answered.Count(t => t.WordCount > 150),
            ai,
            catalog = previous,
            failed,
        };
        return JsonSerializer.SerializeToElement(report);
    }

    private async Task<string?> ProfileText(CancellationToken ct) =>
        (await db.Profiles.AsNoTracking().FirstOrDefaultAsync(ct))?.CvText;
}

