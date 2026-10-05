using System.Text.Json;
using EpamPrep.Api.Ai;
using EpamPrep.Api.Data;
using EpamPrep.Api.Domain;
using EpamPrep.Api.Features.Dashboard;
using EpamPrep.Api.Features.Interview;
using EpamPrep.Api.Features.Lessons;
using EpamPrep.Api.Features.Pronunciation;
using EpamPrep.Api.Features.Quiz;
using EpamPrep.Api.Infrastructure;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EpamPrep.Api;

public sealed record ProfileRequest(string CvText);

public static class Endpoints
{
    public static void MapApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/status", (IClaudeClient ai, IOptions<AnthropicOptions> o, IOptions<InterviewOptions> io, PromptLibrary p) => new
        {
            aiConfigured = ai.IsConfigured,
            demoMode = o.Value.DemoMode,
            provider = ai switch { DemoClaudeClient => "demo", ClaudeCliClient => "cli", _ => "api" },
            model = ai is ClaudeCliClient ? $"Claude Code · {o.Value.Cli.Model}" : o.Value.Model,
            interviewAt = io.Value.Date,
            promptsDirectory = p.Directory,
            setup = SetupHelp(ai),
        });

        api.MapGet("/catalog", () => Catalog.All.Select(c => new
        {
            c.Key, c.Title, c.Previous, c.Target, c.Weak,
            subtopics = c.Subtopics,
        }));

        // ----- Profile -----
        api.MapGet("/profile", async (AppDbContext db, CancellationToken ct) =>
            await db.Profiles.AsNoTracking().FirstOrDefaultAsync(ct) ?? new Profile());

        api.MapPut("/profile", async (ProfileRequest r, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var p = await db.Profiles.FirstOrDefaultAsync(ct);
            if (p is null) db.Profiles.Add(p = new Profile());
            p.CvText = r.CvText ?? "";
            p.UpdatedAt = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
            return p;
        });

        // ----- Interview simulator -----
        var interviews = api.MapGroup("/interviews");

        interviews.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
            await db.InterviewSessions.AsNoTracking()
                .OrderByDescending(s => s.StartedAt)
                .Select(s => new
                {
                    s.Id, s.Mode, s.Topics, s.Status, s.StartedAt, s.EndedAt, s.PlannedMinutes,
                    answers = s.Turns.Count(t => t.Answer != null),
                    hasReport = s.ReportJson != null,
                })
                .Take(50)
                .ToListAsync(ct));

        interviews.MapPost("/", async (StartInterviewRequest r, InterviewService svc, CancellationToken ct) =>
        {
            var s = await svc.StartAsync(r, ct);
            return Results.Ok(new { s.Id });
        });

        interviews.MapGet("/{id:int}", async (int id, InterviewService svc, CancellationToken ct) =>
        {
            var s = await svc.LoadAsync(id, ct);
            if (s is null) return Results.NotFound();
            return Results.Ok(new
            {
                s.Id, s.Mode, s.Status, s.StartedAt, s.EndedAt, s.PlannedMinutes, s.CurrentTopicIndex,
                topics = s.TopicList.Select(k => new { key = k, title = Catalog.Get(k).Title }),
                currentTopic = s.CurrentTopic,
                turns = s.Turns.Select(t => new
                {
                    t.Id, t.Sequence, t.TopicKey, t.Depth, t.Question, t.Answer, t.AnswerSeconds, t.InputMode, t.WordCount,
                    t.Score, t.Level, t.AskedAt,
                    // Full mode keeps feedback hidden until the report exists.
                    assessment = t.AssessmentJson is not null && (s.Mode == "quick" || s.ReportJson is not null)
                        ? AiResultParser.ParseAssessment(JsonDocument.Parse(t.AssessmentJson).RootElement)
                        : null,
                }),
                report = s.ReportJson is null ? (JsonElement?)null : JsonDocument.Parse(s.ReportJson).RootElement,
            });
        });

        interviews.MapPost("/{id:int}/turn", async (
            int id, TurnRequest r, HttpContext http, InterviewService svc, IClaudeClient ai,
            IOptions<JsonOptions> json, ILoggerFactory lf, CancellationToken ct) =>
        {
            if (!ai.IsConfigured) return NotConfigured(ai);

            var sse = SseWriter.Start(http, json.Value.SerializerOptions);
            try
            {
                await svc.RunTurnAsync(id, r, (e, d) => sse.SendAsync(e, d, ct), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Client navigated away.
            }
            catch (Exception ex)
            {
                lf.CreateLogger("Interview").LogError(ex, "Turn failed for session {Id}", id);
                await sse.SendAsync("error", new { message = UserMessage(ex), code = ErrorCode(ex) }, CancellationToken.None);
            }
            return Results.Empty;
        });

        interviews.MapPost("/{id:int}/finish", async (int id, InterviewService svc, IClaudeClient ai, CancellationToken ct) =>
        {
            if (!ai.IsConfigured) return NotConfigured(ai);
            return Results.Ok(await svc.FinishAsync(id, ct));
        });

        interviews.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            await db.InterviewSessions.Where(s => s.Id == id).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        });

        // ----- Quizzes -----
        var quiz = api.MapGroup("/quiz");
        quiz.MapPost("/start", (QuizStartRequest r, QuizService svc, CancellationToken ct) => svc.StartAsync(r, ct));
        quiz.MapPost("/answer", async (QuizAnswerRequest r, QuizService svc, AppDbContext db, IClaudeClient ai, CancellationToken ct) =>
        {
            var q = await db.QuizQuestions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.QuestionId, ct);
            if (q is null) return Results.NotFound();
            var needsAi = q.CorrectOption is null && !(q.Type == "output" && q.ExpectedOutput is not null);
            if (needsAi && !ai.IsConfigured) return NotConfigured(ai);
            return Results.Ok(await svc.AnswerAsync(r, ct));
        });
        quiz.MapPost("/generate", async (GenerateRequest r, QuizService svc, IClaudeClient ai, CancellationToken ct) =>
        {
            if (!ai.IsConfigured) return NotConfigured(ai);
            var created = await svc.GenerateAsync(r, ct);
            return Results.Ok(new { created = created.Count });
        });
        quiz.MapGet("/stats", (QuizService svc, CancellationToken ct) => svc.StatsAsync(ct));

        // ----- Lessons -----
        var lessons = api.MapGroup("/lessons");
        lessons.MapGet("/", (LessonService svc, CancellationToken ct) => svc.IndexAsync(ct));
        lessons.MapGet("/{topic}/{subtopic}", async (string topic, string subtopic, LessonService svc, CancellationToken ct) =>
            await svc.GetAsync(topic, subtopic, ct) is { } l ? Results.Ok(l) : Results.NotFound());
        lessons.MapPost("/{topic}/{subtopic}/generate", async (string topic, string subtopic, LessonService svc, IClaudeClient ai, CancellationToken ct) =>
        {
            if (!ai.IsConfigured) return NotConfigured(ai);
            return Results.Ok(await svc.GenerateAsync(topic, subtopic, ct));
        });

        // ----- Pronunciation -----
        var pron = api.MapGroup("/pronunciation");
        pron.MapGet("/", (PronunciationService svc, CancellationToken ct) => svc.ListAsync(ct));
        pron.MapPost("/", (AddTermRequest r, PronunciationService svc, CancellationToken ct) => svc.AddAsync(r, "manual", ct));
        pron.MapPost("/{id:int}/attempt", (int id, AttemptRequest r, PronunciationService svc, CancellationToken ct) =>
            svc.RecordAttemptAsync(id, r, ct));
        pron.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            await db.PronunciationTerms.Where(t => t.Id == id).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        });

        // ----- Dashboard -----
        api.MapGet("/dashboard", (DashboardService svc, CancellationToken ct) => svc.GetAsync(ct));
        api.MapPost("/plan/toggle", async (TogglePlanTask r, DashboardService svc, CancellationToken ct) =>
        {
            await svc.ToggleAsync(r, ct);
            return Results.NoContent();
        });
    }

    private static readonly string[] ApiSetupHelp =
    [
        "dotnet user-secrets set \"Anthropic:ApiKey\" \"<tu-clave>\" --project src/EpamPrep.Api",
        "o bien: setx ANTHROPIC_API_KEY \"<tu-clave>\"  (abre una terminal nueva después)",
        "Reinicia la app con: npm start",
    ];

    private static readonly string[] CliSetupHelp =
    [
        "Instala Claude Code: https://claude.com/claude-code",
        "Inicia sesión una vez con tu cuenta Claude (Pro/Max): claude   y luego   /login",
        "Si claude no está en el PATH, configura Anthropic:Cli:Path en appsettings.json",
        "Reinicia la app con: npm start",
    ];

    internal static string[] SetupHelp(IClaudeClient ai) => ai is ClaudeCliClient ? CliSetupHelp : ApiSetupHelp;

    private static IResult NotConfigured(IClaudeClient ai) => Results.Json(
        new
        {
            code = "ai_not_configured",
            message = ai is ClaudeCliClient ? "No se encontró Claude Code (claude) en esta máquina." : "Falta ANTHROPIC_API_KEY.",
            setup = SetupHelp(ai),
        },
        statusCode: StatusCodes.Status503ServiceUnavailable);

    internal static string ErrorCode(Exception ex) => ex switch
    {
        AiNotConfiguredException => "ai_not_configured",
        AiLoginRequiredException => "ai_login_required",
        AiRefusalException => "refusal",
        AiException => "ai_error",
        Anthropic.Exceptions.AnthropicApiException => "ai_error",
        ArgumentException or InvalidOperationException or KeyNotFoundException => "bad_request",
        _ => "unexpected",
    };

    internal static string UserMessage(Exception ex) => ex switch
    {
        AiException or ArgumentException or InvalidOperationException or KeyNotFoundException => ex.Message,
        Anthropic.Exceptions.AnthropicApiException api => $"Error de la API de Anthropic: {api.Message}",
        _ => "Error inesperado. Revisa la consola del backend.",
    };
}
