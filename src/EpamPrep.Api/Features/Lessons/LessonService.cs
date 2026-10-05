using System.Text;
using System.Text.Json;
using EpamPrep.Api.Ai;
using EpamPrep.Api.Data;
using EpamPrep.Api.Domain;
using EpamPrep.Api.Features.Pronunciation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EpamPrep.Api.Features.Lessons;

public sealed class LessonService(
    AppDbContext db,
    IClaudeClient ai,
    PromptLibrary prompts,
    PronunciationService pronunciation,
    IOptions<AnthropicOptions> aiOptions,
    TimeProvider clock)
{
    public async Task<object> IndexAsync(CancellationToken ct)
    {
        var existing = await db.Lessons.AsNoTracking()
            .Select(l => new { l.TopicKey, l.SubtopicKey, l.CreatedAt }).ToListAsync(ct);
        return Catalog.All.Select(c => new
        {
            key = c.Key,
            title = c.Title,
            subtopics = c.Subtopics.Select(s => new
            {
                key = s.Key,
                title = s.Title,
                generatedAt = existing.FirstOrDefault(e => e.TopicKey == c.Key && e.SubtopicKey == s.Key)?.CreatedAt,
            }),
        });
    }

    public async Task<JsonElement?> GetAsync(string topic, string subtopic, CancellationToken ct)
    {
        var lesson = await db.Lessons.AsNoTracking()
            .FirstOrDefaultAsync(l => l.TopicKey == topic && l.SubtopicKey == subtopic, ct);
        return lesson is null ? null : Wrap(lesson);
    }

    /// <summary>Generates (or regenerates) a lesson and stores it so it is not paid for twice.</summary>
    public async Task<JsonElement> GenerateAsync(string topic, string subtopic, CancellationToken ct)
    {
        var c = Catalog.Get(topic);
        var s = c.Subtopics.FirstOrDefault(x => x.Key == subtopic)
                ?? throw new KeyNotFoundException($"Subtema desconocido: {subtopic}");

        var sb = new StringBuilder();
        sb.AppendLine($"Competency: {c.Title} ({c.Key}). Previous EPAM level: {c.Previous}. Target: {c.Target}.");
        sb.AppendLine($"Subtopic: {s.Title}");
        sb.AppendLine($"Sibling subtopics (covered in other lessons, mention only if needed): {string.Join("; ", c.Subtopics.Where(x => x.Key != s.Key).Select(x => x.Title))}");

        using var doc = await ai.GetJsonAsync(
            new AiRequest(prompts.SystemFor("lesson", profile: null), [AiMessage.User(sb.ToString())],
                Effort: aiOptions.Value.Effort.Lesson, MaxTokens: 24000),
            prompts.Schema("lesson"), ct);

        var json = doc.RootElement.GetRawText();
        var lesson = await db.Lessons.FirstOrDefaultAsync(l => l.TopicKey == topic && l.SubtopicKey == subtopic, ct);
        if (lesson is null)
        {
            lesson = new Lesson { TopicKey = topic, SubtopicKey = subtopic };
            db.Lessons.Add(lesson);
        }
        lesson.ContentJson = json;
        lesson.CreatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);

        if (doc.RootElement.TryGetProperty("key_terms", out var terms) && terms.ValueKind == JsonValueKind.Array)
        {
            await pronunciation.AddFromLessonAsync(terms.EnumerateArray().Select(t => new KeyTermInput(
                t.GetProperty("term").GetString() ?? "",
                t.GetProperty("respelling").GetString() ?? "",
                t.TryGetProperty("ipa", out var ipa) ? ipa.GetString() : null,
                t.TryGetProperty("tip_es", out var tip) ? tip.GetString() : null)), ct);
        }

        return Wrap(lesson);
    }

    private static JsonElement Wrap(Lesson l) => JsonSerializer.SerializeToElement(new
    {
        topicKey = l.TopicKey,
        subtopicKey = l.SubtopicKey,
        createdAt = l.CreatedAt,
        content = JsonDocument.Parse(l.ContentJson).RootElement,
    });
}
