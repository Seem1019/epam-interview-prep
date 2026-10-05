using System.Text;
using System.Text.Json;
using EpamPrep.Api.Ai;
using EpamPrep.Api.Data;
using EpamPrep.Api.Domain;
using EpamPrep.Api.Features.Dashboard;
using EpamPrep.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EpamPrep.Api.Features.Quiz;

public sealed record QuizStartRequest(string? TopicKey, string? Level, int Count = 8, bool ReviewsOnly = false, bool GenerateIfNeeded = true);

public sealed record QuizAnswerRequest(int QuestionId, string? Answer, int? OptionIndex, bool WasReview);

public sealed record GenerateRequest(string TopicKey, string Level, int Count = 5, string? Type = null);

public sealed record QuestionDto(
    int Id, string TopicKey, string TopicTitle, string? Subtopic, string Level, string Type,
    string Prompt, string? Code, string? CodeLang, IReadOnlyList<string> Options, bool IsReview, string Source);

public sealed class QuizService(
    AppDbContext db,
    IClaudeClient ai,
    PromptLibrary prompts,
    IOptions<AnthropicOptions> aiOptions,
    TimeProvider clock)
{
    private readonly EffortOptions _effort = aiOptions.Value.Effort;

    public async Task<object> StartAsync(QuizStartRequest r, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var count = Math.Clamp(r.Count, 1, 20);
        var picked = new List<(QuizQuestion Q, bool Review)>();

        // 1) Due reviews first: failed questions come back on schedule.
        var dueQuery = db.ReviewItems.Include(x => x.Question).Where(x => x.DueAt <= now);
        if (r.TopicKey is not null) dueQuery = dueQuery.Where(x => x.Question!.TopicKey == r.TopicKey);
        var due = await dueQuery.OrderBy(x => x.DueAt).Take(r.ReviewsOnly ? count : Math.Max(1, count / 2)).ToListAsync(ct);
        picked.AddRange(due.Select(d => (d.Question!, true)));

        if (!r.ReviewsOnly)
        {
            // 2) Questions never attempted, then the weakest ones.
            var pool = db.QuizQuestions.AsQueryable();
            if (r.TopicKey is not null) pool = pool.Where(q => q.TopicKey == r.TopicKey);
            if (r.Level is not null) pool = pool.Where(q => q.Level == r.Level);
            var exclude = picked.Select(p => p.Q.Id).ToHashSet();

            var unseen = await pool.Where(q => !db.QuizAttempts.Any(a => a.QuestionId == q.Id)).ToListAsync(ct);
            picked.AddRange(unseen.Where(q => !exclude.Contains(q.Id)).OrderBy(_ => Random.Shared.Next())
                .Take(count - picked.Count).Select(q => (q, false)));

            if (picked.Count < count && r.GenerateIfNeeded && ai.IsConfigured && r.TopicKey is not null)
            {
                var generated = await GenerateAsync(
                    new GenerateRequest(r.TopicKey, r.Level ?? LevelForGeneration(r.TopicKey), Math.Min(5, count - picked.Count)), ct);
                picked.AddRange(generated.Select(q => (q, false)));
            }

            if (picked.Count < count)
            {
                exclude = picked.Select(p => p.Q.Id).ToHashSet();
                var weakest = await pool
                    .Select(q => new { q, avg = db.QuizAttempts.Where(a => a.QuestionId == q.Id).Average(a => (double?)a.Score) ?? 0 })
                    .OrderBy(x => x.avg)
                    .Take(count * 2)
                    .ToListAsync(ct);
                picked.AddRange(weakest.Where(x => !exclude.Contains(x.q.Id)).Take(count - picked.Count).Select(x => (x.q, false)));
            }
        }

        return new
        {
            questions = picked.Take(count).Select(p => ToDto(p.Q, p.Review)).ToList(),
            aiConfigured = ai.IsConfigured,
        };
    }

    public async Task<object> AnswerAsync(QuizAnswerRequest r, CancellationToken ct)
    {
        var q = await db.QuizQuestions.FindAsync([r.QuestionId], ct) ?? throw new KeyNotFoundException("Pregunta no encontrada.");
        var now = clock.GetUtcNow().UtcDateTime;
        int score;
        QuizGrade? grade = null;
        string answerText;

        if (q.CorrectOption is not null && q.OptionsJson is not null)
        {
            if (r.OptionIndex is null) throw new ArgumentException("Elige una opción.");
            var options = JsonSerializer.Deserialize<List<string>>(q.OptionsJson) ?? [];
            answerText = r.OptionIndex >= 0 && r.OptionIndex < options.Count ? options[r.OptionIndex.Value] : "";
            score = r.OptionIndex == q.CorrectOption ? 100 : 0;
        }
        else if (q.Type == "output" && q.ExpectedOutput is not null)
        {
            answerText = r.Answer ?? "";
            score = NormalizeOutput(answerText) == NormalizeOutput(q.ExpectedOutput) ? 100 : 0;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(r.Answer)) throw new ArgumentException("Escribe una respuesta.");
            answerText = r.Answer.Trim();
            using var doc = await GradeAsync(q, answerText, ct);
            grade = AiResultParser.ParseGrade(doc.RootElement);
            score = grade.Score;
        }

        var passed = score >= QuizScoring.PassScore;
        db.QuizAttempts.Add(new QuizAttempt
        {
            QuestionId = q.Id,
            TopicKey = q.TopicKey,
            Level = q.Level,
            Answer = answerText,
            Score = score,
            Passed = passed,
            FeedbackJson = grade is null ? null : JsonSerializer.Serialize(grade),
            WasReview = r.WasReview,
            CreatedAt = now,
        });

        var review = await db.ReviewItems.FirstOrDefaultAsync(x => x.QuestionId == q.Id, ct);
        var quality = QuizScoring.ToQuality(score);
        if (review is not null)
        {
            SpacedRepetition.Apply(review, quality, now);
        }
        else if (!passed)
        {
            review = SpacedRepetition.StartAfterFailure(q.Id, quality, now);
            db.ReviewItems.Add(review);
        }
        await db.SaveChangesAsync(ct);

        return new
        {
            score,
            passed,
            correctOption = q.CorrectOption,
            expectedOutput = q.ExpectedOutput,
            explanationEs = q.ExplanationEs,
            modelAnswerEn = grade?.ModelAnswerEn is { Length: > 0 } m ? m : q.ModelAnswer,
            grade,
            nextReviewAt = review?.DueAt,
        };
    }

    public async Task<List<QuizQuestion>> GenerateAsync(GenerateRequest r, CancellationToken ct)
    {
        var competency = Catalog.Get(r.TopicKey);
        var existing = await db.QuizQuestions.Where(q => q.TopicKey == r.TopicKey)
            .OrderByDescending(q => q.Id).Select(q => q.Prompt).Take(40).ToListAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine($"Competency: {competency.Title} ({competency.Key})");
        sb.AppendLine($"Level: {r.Level}");
        sb.AppendLine($"Number of questions: {Math.Clamp(r.Count, 1, 8)}");
        sb.AppendLine(r.Type is null
            ? "Types: a mix; include at least one snippet-based question (output or code_review) when the competency involves code."
            : $"Type: {r.Type} only.");
        sb.AppendLine($"Subtopics: {string.Join("; ", competency.Subtopics.Select(s => s.Title))}");
        if (existing.Count > 0)
        {
            sb.AppendLine("Avoid duplicates of these existing questions:");
            foreach (var e in existing) sb.AppendLine("- " + e);
        }

        using var doc = await ai.GetJsonAsync(
            new AiRequest(prompts.SystemFor("quiz-generator", profile: null), [AiMessage.User(sb.ToString())],
                Effort: _effort.Generator, MaxTokens: 24000),
            prompts.Schema("questions"), ct);

        var now = clock.GetUtcNow().UtcDateTime;
        var entities = AiResultParser.ParseQuestions(doc.RootElement).Select(g => new QuizQuestion
        {
            TopicKey = competency.Key,
            Subtopic = g.Subtopic,
            Level = g.Level.ToString(),
            Type = g.Type,
            Prompt = g.PromptEn,
            Code = g.Code,
            CodeLang = g.CodeLang,
            OptionsJson = g.Options.Count > 0 ? JsonSerializer.Serialize(g.Options) : null,
            CorrectOption = g.CorrectOption,
            ExpectedOutput = g.ExpectedOutput,
            ModelAnswer = g.ModelAnswerEn,
            ExplanationEs = g.ExplanationEs,
            Source = "ai",
            CreatedAt = now,
        }).ToList();

        db.QuizQuestions.AddRange(entities);
        await db.SaveChangesAsync(ct);
        return entities;
    }

    /// <summary>Per-competency score and quiz-based level estimate (feeds the dashboard).</summary>
    public async Task<List<TopicStats>> StatsAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var attempts = await db.QuizAttempts.AsNoTracking().OrderBy(a => a.CreatedAt)
            .Select(a => new { a.TopicKey, a.Level, a.Score }).ToListAsync(ct);
        var dueByTopic = await db.ReviewItems.Where(r => r.DueAt <= now)
            .GroupBy(r => r.Question!.TopicKey).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var questionCounts = await db.QuizQuestions.GroupBy(q => q.TopicKey)
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);

        return Catalog.All.Select(c =>
        {
            var mine = attempts.Where(a => a.TopicKey == c.Key).ToList();
            var recent = mine.TakeLast(20).ToList();
            return new TopicStats(
                c.Key,
                c.Title,
                mine.Count,
                recent.Count == 0 ? null : (int)Math.Round(recent.Average(a => a.Score)),
                LevelEstimator.FromQuiz(mine.Select(a => (LevelExtensions.ParseLevel(a.Level), a.Score))),
                dueByTopic.FirstOrDefault(d => d.Key == c.Key)?.Count ?? 0,
                questionCounts.FirstOrDefault(d => d.Key == c.Key)?.Count ?? 0);
        }).ToList();
    }

    private async Task<JsonDocument> GradeAsync(QuizQuestion q, string answer, CancellationToken ct)
    {
        var c = Catalog.Get(q.TopicKey);
        var sb = new StringBuilder();
        sb.AppendLine($"Competency: {c.Title} ({c.Key}). Question level: {q.Level}. Type: {q.Type}.");
        sb.AppendLine();
        sb.AppendLine("## Question");
        sb.AppendLine(q.Prompt);
        if (q.Code is not null)
        {
            sb.AppendLine($"```{q.CodeLang}");
            sb.AppendLine(q.Code);
            sb.AppendLine("```");
        }
        sb.AppendLine();
        sb.AppendLine("## Reference answer (guide, not the only valid answer)");
        sb.AppendLine(q.ModelAnswer ?? "(none)");
        sb.AppendLine();
        sb.AppendLine($"## Candidate's answer ({TextStats.WordCount(answer)} words)");
        sb.AppendLine(answer);

        return await ai.GetJsonAsync(
            new AiRequest(prompts.SystemFor("quiz-grader", profile: null), [AiMessage.User(sb.ToString())],
                Effort: _effort.Grader, MaxTokens: 8000),
            prompts.Schema("grade"), ct);
    }

    private string LevelForGeneration(string topicKey)
    {
        var c = Catalog.Get(topicKey);
        // Aim one notch above the previous result, capped by the target.
        var next = (Level)Math.Min((int)c.Target, Math.Max((int)Level.Novice, (int)c.Previous + 1));
        return next.ToString();
    }

    internal static string NormalizeOutput(string s) =>
        string.Join(' ', s.Split((char[])[' ', '\n', '\r', '\t', ','], StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    private static QuestionDto ToDto(QuizQuestion q, bool review) => new(
        q.Id, q.TopicKey, Catalog.Get(q.TopicKey).Title, q.Subtopic, q.Level, q.Type, q.Prompt, q.Code, q.CodeLang,
        q.OptionsJson is null ? [] : JsonSerializer.Deserialize<List<string>>(q.OptionsJson) ?? [],
        review, q.Source);
}

public sealed record TopicStats(
    string Key, string Title, int Attempts, int? AverageScore, Level QuizLevel, int DueReviews, int QuestionCount);
