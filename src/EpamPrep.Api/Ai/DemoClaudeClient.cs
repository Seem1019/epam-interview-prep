using System.Runtime.CompilerServices;
using System.Text.Json;

namespace EpamPrep.Api.Ai;

/// <summary>
/// Canned responses that satisfy each schema, enabled with Anthropic:DemoMode=true.
/// Only for exercising the UI end to end without an API key; it never grades anything for real.
/// </summary>
public sealed class DemoClaudeClient : IClaudeClient
{
    private static readonly string[] Questions =
    [
        "Hi, thanks for joining. We'll start with the basics and go deeper on each topic. To begin: what is the difference between a value type and a reference type in C#?",
        "Okay. Let's go a bit deeper. What does this print, and why?\n\n```csharp\nvar list = new List<int> { 1, 2, 3 };\nvar query = list.Where(x => x > 1);\nlist.Add(4);\nConsole.WriteLine(query.Count());\n```",
        "Got it. In production, what would you check first if an async endpoint starts exhausting the thread pool?",
        "Okay, let's switch topics. How does the ASP.NET Core middleware pipeline work?",
    ];

    private int _turn;

    public bool IsConfigured => true;

    public async IAsyncEnumerable<string> StreamTextAsync(AiRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var last = request.Messages[^1].Content;
        var text = last.Contains("interview is over", StringComparison.OrdinalIgnoreCase)
            ? "Thanks a lot for your time today, that's all from my side."
            : Questions[Interlocked.Increment(ref _turn) % Questions.Length];
        foreach (var word in text.Split(' '))
        {
            await Task.Delay(25, ct);
            yield return word + " ";
        }
    }

    public Task<JsonDocument> GetJsonAsync(AiRequest request, JsonElement schema, CancellationToken ct = default)
    {
        var props = schema.GetProperty("properties");
        object result =
            props.TryGetProperty("decision", out _) ? new
            {
                level_demonstrated = "Intermediate",
                score = 6,
                justification_es = "[DEMO] Respuesta correcta a nivel de uso, sin internals ni trade-offs.",
                decision = "go_deeper",
                next_focus = "ask about internals",
                feedback_es = "[DEMO] Este feedback es simulado. Configura ANTHROPIC_API_KEY para una evaluación real.",
                concision = new { verdict = "acceptable", comment_es = "[DEMO] Longitud razonable.", short_version_en = "" },
                model_answer_en = "[DEMO] A value type holds its data directly, usually on the stack or inline; a reference type holds a reference to an object on the heap.",
                misrecognized_terms = Array.Empty<object>(),
            } :
            props.TryGetProperty("missing_points_es", out _) ? new
            {
                score = 65,
                verdict = "partial",
                level_demonstrated = "Intermediate",
                justification_es = "[DEMO] Calificación simulada.",
                missing_points_es = new[] { "[DEMO] Configura la API key para calificar de verdad." },
                concision = new { verdict = "acceptable", comment_es = "", short_version_en = "" },
                model_answer_en = "[DEMO] Model answer.",
            } :
            props.TryGetProperty("competencies", out _) ? new
            {
                summary_es = "[DEMO] Reporte simulado.",
                overall_level = "Intermediate",
                competencies = new[] { new { key = "csharp", level = "Intermediate", evidence_es = "[DEMO]", to_next_level_es = "[DEMO]", comparison_es = "[DEMO]" } },
                communication = new { concision_es = "[DEMO]", pronunciation_es = "[DEMO]" },
                top_actions_es = new[] { "[DEMO] Configurar la API key." },
            } :
            props.TryGetProperty("key_terms", out _) ? new
            {
                title = "[DEMO] Lesson",
                explanation = new
                {
                    definition_es = "[DEMO] Un access modifier decide quién puede ver un tipo o un miembro.",
                    when_why_es = "[DEMO] Se usa para exponer solo lo necesario y poder cambiar lo interno sin romper a otros.",
                    trade_off_es = "[DEMO] Más restricción = más seguridad al cambiar, pero menos flexibilidad para quien consume.",
                    example_es = "[DEMO] Un repositorio expone métodos public y guarda la conexión como private.",
                    example_code = "public class Repo\n{\n    private readonly string _cs = \"...\";\n    public Task SaveAsync() => Task.CompletedTask;\n}",
                    example_lang = "csharp",
                },
                key_concepts = new[] { new { name = "public", plain_es = "[DEMO] Visible para todos.", use_when_es = "[DEMO] El contrato de tu API." } },
                glossary = new[] { new { term = "assembly", meaning_es = "[DEMO] El .dll que genera un proyecto." } },
                senior_details_es = new[] { "[DEMO]" },
                code_examples = new[] { new { title = "Example", lang = "csharp", code = "Console.WriteLine(\"demo\");", explanation_es = "[DEMO]" } },
                common_mistakes_es = new[] { "[DEMO]" },
                interview_qa = new[] { new { question_en = "[DEMO] Question?", answer_en = "[DEMO] Answer." } },
                key_terms = new[] { new { term = "cache", respelling = "KASH", ipa = "kæʃ", tip_es = "[DEMO]" } },
            } :
            new { questions = Array.Empty<object>() };

        return Task.FromResult(JsonSerializer.SerializeToDocument(result));
    }
}
