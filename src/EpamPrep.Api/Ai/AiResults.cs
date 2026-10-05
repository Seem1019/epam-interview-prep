using System.Text.Json;
using EpamPrep.Api.Domain;
using EpamPrep.Api.Features.Quiz;

namespace EpamPrep.Api.Ai;

public sealed record Concision(string Verdict, string CommentEs, string ShortVersionEn);

public sealed record MisrecognizedTerm(string Heard, string Intended);

/// <summary>Evaluator output for one interview answer.</summary>
public sealed record AnswerAssessment(
    Level LevelDemonstrated,
    int Score,
    string JustificationEs,
    string Decision,
    string NextFocus,
    string FeedbackEs,
    Concision Concision,
    string ModelAnswerEn,
    IReadOnlyList<MisrecognizedTerm> MisrecognizedTerms);

/// <summary>Grader output for an open / code-review quiz answer.</summary>
public sealed record QuizGrade(
    int Score,
    string Verdict,
    Level LevelDemonstrated,
    string JustificationEs,
    IReadOnlyList<string> MissingPointsEs,
    Concision Concision,
    string ModelAnswerEn);

public sealed record GeneratedQuestion(
    string Type,
    Level Level,
    string Subtopic,
    string PromptEn,
    string? Code,
    string? CodeLang,
    IReadOnlyList<string> Options,
    int? CorrectOption,
    string? ExpectedOutput,
    string ModelAnswerEn,
    string ExplanationEs);

/// <summary>
/// Converts model JSON into typed results. Structured outputs guarantee the shape,
/// but values are still clamped and normalised here (defence in depth: schemas can be
/// edited by hand in prompts/schemas, and numeric ranges are not enforced by the API).
/// </summary>
public static class AiResultParser
{
    public static readonly string[] Decisions = ["go_deeper", "clarify", "next_topic"];
    private static readonly string[] ConcisionVerdicts = ["concise", "acceptable", "too_long", "too_short"];
    private static readonly string[] QuestionTypes = ["multiple_choice", "output", "open", "code_review"];

    public static AnswerAssessment ParseAssessment(JsonElement root) => new(
        LevelExtensions.ParseLevel(Str(root, "level_demonstrated")),
        Math.Clamp(Int(root, "score"), 0, 10),
        Str(root, "justification_es"),
        OneOf(Str(root, "decision"), Decisions, fallback: "next_topic"),
        Str(root, "next_focus"),
        Str(root, "feedback_es"),
        ParseConcision(root),
        Str(root, "model_answer_en"),
        Array(root, "misrecognized_terms")
            .Select(t => new MisrecognizedTerm(Str(t, "heard"), Str(t, "intended")))
            .Where(t => t.Intended.Length > 0)
            .ToList());

    public static QuizGrade ParseGrade(JsonElement root)
    {
        var score = Math.Clamp(Int(root, "score"), 0, 100);
        // The verdict is derived from the score so the two can never contradict each other.
        var verdict = score >= QuizScoring.PassScore ? "correct" : score >= 40 ? "partial" : "incorrect";
        return new QuizGrade(
            score,
            verdict,
            LevelExtensions.ParseLevel(Str(root, "level_demonstrated")),
            Str(root, "justification_es"),
            Array(root, "missing_points_es").Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList(),
            ParseConcision(root),
            Str(root, "model_answer_en"));
    }

    public static IReadOnlyList<GeneratedQuestion> ParseQuestions(JsonElement root)
    {
        var result = new List<GeneratedQuestion>();
        foreach (var q in Array(root, "questions"))
        {
            var type = OneOf(Str(q, "type"), QuestionTypes, fallback: "open");
            var options = Array(q, "options").Select(o => o.GetString() ?? "").ToList();
            int? correct = Int(q, "correct_option");
            var hasOptions = type is "multiple_choice" or "output";

            if (hasOptions && (options.Count < 2 || correct < 0 || correct >= options.Count))
            {
                // Unusable choice question (bad index): keep it as an open question instead of losing it.
                type = "open";
            }
            if (type is "open" or "code_review")
            {
                options = [];
                correct = null;
            }

            var level = LevelExtensions.ParseLevel(Str(q, "level"));
            var lang = Str(q, "code_lang");
            var code = Str(q, "code");
            result.Add(new GeneratedQuestion(
                type,
                level == Level.None ? Level.Intermediate : level,
                Str(q, "subtopic"),
                Str(q, "prompt_en"),
                string.IsNullOrWhiteSpace(code) ? null : code,
                lang is "" or "none" ? null : lang,
                options,
                correct,
                NullIfEmpty(Str(q, "expected_output")),
                Str(q, "model_answer_en"),
                Str(q, "explanation_es")));
        }
        return result.Where(q => q.PromptEn.Length > 0).ToList();
    }

    private static Concision ParseConcision(JsonElement root)
    {
        if (!root.TryGetProperty("concision", out var c) || c.ValueKind != JsonValueKind.Object)
        {
            return new Concision("acceptable", "", "");
        }
        var verdict = OneOf(Str(c, "verdict"), ConcisionVerdicts, fallback: "acceptable");
        var shortVersion = verdict == "too_long" ? Str(c, "short_version_en") : "";
        return new Concision(verdict, Str(c, "comment_es"), shortVersion);
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";

    private static int Int(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt32(out var i) => i,
            JsonValueKind.Number => (int)Math.Round(v.GetDouble()),
            JsonValueKind.String when int.TryParse(v.GetString(), out var s) => s,
            _ => 0,
        };
    }

    private static IEnumerable<JsonElement> Array(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : [];

    private static string OneOf(string value, string[] allowed, string fallback) =>
        allowed.Contains(value, StringComparer.OrdinalIgnoreCase) ? value.ToLowerInvariant() : fallback;

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;
}
