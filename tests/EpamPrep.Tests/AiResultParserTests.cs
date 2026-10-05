using System.Text.Json;
using EpamPrep.Api.Ai;
using EpamPrep.Api.Domain;

namespace EpamPrep.Tests;

public class AiResultParserTests
{
    private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement;

    [Fact]
    public void ParseAssessment_ReadsAllFields()
    {
        var a = AiResultParser.ParseAssessment(Json("""
            {
              "level_demonstrated": "Advanced", "score": 8, "justification_es": "Explicó el state machine.",
              "decision": "go_deeper", "next_focus": "ConfigureAwait", "feedback_es": "Bien.",
              "concision": { "verdict": "too_long", "comment_es": "180 palabras.", "short_version_en": "Short." },
              "model_answer_en": "Model.", "misrecognized_terms": [ { "heard": "a sure", "intended": "Azure" } ]
            }
            """));

        Assert.Equal(Level.Advanced, a.LevelDemonstrated);
        Assert.Equal(8, a.Score);
        Assert.Equal("go_deeper", a.Decision);
        Assert.Equal("too_long", a.Concision.Verdict);
        Assert.Equal("Short.", a.Concision.ShortVersionEn);
        Assert.Equal(new MisrecognizedTerm("a sure", "Azure"), Assert.Single(a.MisrecognizedTerms));
    }

    [Fact]
    public void ParseAssessment_ClampsScore_AndFallsBackOnUnknownValues()
    {
        var a = AiResultParser.ParseAssessment(Json("""
            { "level_demonstrated": "Guru", "score": 42, "decision": "ask_again",
              "concision": { "verdict": "acceptable", "comment_es": "", "short_version_en": "should be dropped" },
              "misrecognized_terms": [ { "heard": "x", "intended": "" } ] }
            """));

        Assert.Equal(10, a.Score);
        Assert.Equal(Level.None, a.LevelDemonstrated);
        Assert.Equal("next_topic", a.Decision); // unknown decision → safest option: move on
        Assert.Equal("", a.Concision.ShortVersionEn); // only kept when the answer was too long
        Assert.Empty(a.MisrecognizedTerms);
        Assert.Equal("", a.JustificationEs);
    }

    [Fact]
    public void ParseAssessment_AcceptsNumericStrings()
    {
        var a = AiResultParser.ParseAssessment(Json("""{ "score": "7", "decision": "CLARIFY" }"""));
        Assert.Equal(7, a.Score);
        Assert.Equal("clarify", a.Decision);
    }

    [Theory]
    [InlineData(95, "correct")]
    [InlineData(70, "correct")]
    [InlineData(69, "partial")]
    [InlineData(40, "partial")]
    [InlineData(10, "incorrect")]
    [InlineData(250, "correct")]
    public void ParseGrade_DerivesVerdictFromScore(int score, string expected)
    {
        // The model says "incorrect" on purpose: the verdict must follow the score, never contradict it.
        var g = AiResultParser.ParseGrade(Json($$"""{ "score": {{score}}, "verdict": "incorrect", "missing_points_es": ["a", ""] }"""));
        Assert.Equal(expected, g.Verdict);
        Assert.InRange(g.Score, 0, 100);
        Assert.Equal(["a"], g.MissingPointsEs);
    }

    [Fact]
    public void ParseQuestions_DowngradesChoiceQuestionWithInvalidIndex_ToOpen()
    {
        var qs = AiResultParser.ParseQuestions(Json("""
            { "questions": [
              { "type": "multiple_choice", "level": "Advanced", "subtopic": "s", "prompt_en": "Q1", "code": "", "code_lang": "none",
                "options": ["a", "b"], "correct_option": 5, "expected_output": "", "model_answer_en": "m", "explanation_es": "e" },
              { "type": "output", "level": "Intermediate", "subtopic": "s", "prompt_en": "Q2", "code": "x", "code_lang": "csharp",
                "options": ["1", "2", "3"], "correct_option": 1, "expected_output": "2", "model_answer_en": "m", "explanation_es": "e" },
              { "type": "open", "level": "Bogus", "subtopic": "s", "prompt_en": "", "code": "", "code_lang": "none",
                "options": [], "correct_option": -1, "expected_output": "", "model_answer_en": "", "explanation_es": "" }
            ] }
            """));

        Assert.Equal(2, qs.Count); // the one with an empty prompt is dropped
        Assert.Equal("open", qs[0].Type);
        Assert.Empty(qs[0].Options);
        Assert.Null(qs[0].CorrectOption);
        Assert.Null(qs[0].CodeLang);

        Assert.Equal("output", qs[1].Type);
        Assert.Equal(1, qs[1].CorrectOption);
        Assert.Equal("csharp", qs[1].CodeLang);
        Assert.Equal("2", qs[1].ExpectedOutput);
    }
}
