using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace EpamPrep.Api.Data;

/// <summary>Loads the seed question bank and pronunciation terms the first time the database is created.</summary>
public static class DataSeeder
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        var seedDir = Path.Combine(AppContext.BaseDirectory, "Data", "Seed");
        var now = DateTime.UtcNow;

        if (!await db.QuizQuestions.AnyAsync(q => q.Source == "seed", ct))
        {
            var items = Read<SeedQuestion>(Path.Combine(seedDir, "questions.json"));
            db.QuizQuestions.AddRange(items.Select(s => new QuizQuestion
            {
                TopicKey = s.Topic,
                Subtopic = s.Subtopic,
                Level = s.Level,
                Type = s.Type,
                Prompt = s.Prompt,
                Code = string.IsNullOrWhiteSpace(s.Code) ? null : s.Code,
                CodeLang = s.Lang is null or "none" ? null : s.Lang,
                OptionsJson = s.Options is { Count: > 0 } ? JsonSerializer.Serialize(s.Options) : null,
                CorrectOption = s.Options is { Count: > 0 } ? s.Correct : null,
                ExpectedOutput = s.Expected,
                ModelAnswer = s.Model,
                ExplanationEs = s.Explanation,
                Source = "seed",
                CreatedAt = now,
            }));
        }

        if (!await db.PronunciationTerms.AnyAsync(ct))
        {
            var terms = Read<SeedTerm>(Path.Combine(seedDir, "pronunciation.json"));
            db.PronunciationTerms.AddRange(terms.Select(t => new PronunciationTerm
            {
                Term = t.Term,
                Respelling = t.Respelling,
                Ipa = t.Ipa,
                TipEs = t.Tip,
                Accepted = t.Accepted,
                Source = "seed",
                CreatedAt = now,
            }));
        }

        await db.SaveChangesAsync(ct);
    }

    private static List<T> Read<T>(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path), Json) ?? [] : [];

    private sealed record SeedQuestion(
        string Topic, string Level, string Type, string? Subtopic, string Prompt, string? Lang, string? Code,
        List<string>? Options, int? Correct, string? Expected, string? Model, string? Explanation);

    private sealed record SeedTerm(string Term, string Respelling, string? Ipa, string? Tip, string Accepted);
}
