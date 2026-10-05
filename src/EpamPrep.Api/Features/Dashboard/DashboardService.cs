using System.Text.Json;
using EpamPrep.Api.Ai;
using EpamPrep.Api.Data;
using EpamPrep.Api.Domain;
using EpamPrep.Api.Features.Interview;
using EpamPrep.Api.Features.Quiz;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EpamPrep.Api.Features.Dashboard;

public sealed record TogglePlanTask(string Key, bool Done);

public sealed class DashboardService(
    AppDbContext db,
    QuizService quiz,
    IClaudeClient ai,
    IOptions<InterviewOptions> interviewOptions,
    TimeProvider clock)
{
    public async Task<object> GetAsync(CancellationToken ct)
    {
        var interviewAt = interviewOptions.Value.Date;
        var localNow = clock.GetLocalNow().DateTime;
        var stats = await quiz.StatsAsync(ct);
        var interviewLevels = await LatestInterviewLevelsAsync(ct);
        var profile = await db.Profiles.AsNoTracking().FirstOrDefaultAsync(ct);
        var profileComplete = !string.IsNullOrWhiteSpace(profile?.CvText);
        var fullMockDone = await db.InterviewSessions.AnyAsync(s => s.Mode == "full" && s.ReportJson != null, ct);
        var doneKeys = (await db.PlanTasks.Where(p => p.Done).Select(p => p.Key).ToListAsync(ct)).ToHashSet();

        var rows = Catalog.All.Select(c =>
        {
            var s = stats.First(x => x.Key == c.Key);
            interviewLevels.TryGetValue(c.Key, out var fromInterview);
            var current = fromInterview != Level.None ? fromInterview
                : s.QuizLevel != Level.None ? s.QuizLevel
                : c.Previous;
            return new
            {
                key = c.Key,
                title = c.Title,
                previous = c.Previous,
                quizLevel = s.QuizLevel,
                interviewLevel = fromInterview,
                current,
                currentSource = fromInterview != Level.None ? "simulacro" : s.QuizLevel != Level.None ? "quizzes" : "anterior",
                target = c.Target,
                weak = c.Weak,
                quizAverage = s.AverageScore,
                attempts = s.Attempts,
                dueReviews = s.DueReviews,
            };
        }).ToList();

        var plan = StudyPlanner.Build(
            DateOnly.FromDateTime(localNow),
            DateOnly.FromDateTime(interviewAt),
            rows.Select(r => new CompetencyStatus(r.key, r.title, r.previous, r.current, r.target, r.weak, r.quizAverage)).ToList(),
            doneKeys,
            profileComplete,
            fullMockDone);

        return new
        {
            interviewAt,
            now = localNow,
            aiConfigured = ai.IsConfigured,
            profileComplete,
            dueReviews = stats.Sum(s => s.DueReviews),
            competencies = rows,
            plan,
        };
    }

    public async Task ToggleAsync(TogglePlanTask request, CancellationToken ct)
    {
        var task = await db.PlanTasks.FindAsync([request.Key], ct);
        if (task is null)
        {
            task = new PlanTaskState { Key = request.Key };
            db.PlanTasks.Add(task);
        }
        task.Done = request.Done;
        task.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Latest ceiling per competency from finished mock interviews.</summary>
    private async Task<Dictionary<string, Level>> LatestInterviewLevelsAsync(CancellationToken ct)
    {
        var reports = await db.InterviewSessions.AsNoTracking()
            .Where(s => s.ReportJson != null)
            .OrderBy(s => s.StartedAt)
            .Select(s => s.ReportJson!)
            .ToListAsync(ct);

        var result = new Dictionary<string, Level>();
        foreach (var json in reports)
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("ai", out var aiPart) ||
                !aiPart.TryGetProperty("competencies", out var comps)) continue;
            foreach (var c in comps.EnumerateArray())
            {
                var key = c.GetProperty("key").GetString();
                if (key is not null && Catalog.Find(key) is not null)
                {
                    result[key] = LevelExtensions.ParseLevel(c.GetProperty("level").GetString());
                }
            }
        }
        return result;
    }
}
