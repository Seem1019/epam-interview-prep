using System.Text.RegularExpressions;
using EpamPrep.Api.Ai;
using EpamPrep.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EpamPrep.Api.Features.Pronunciation;

public sealed record AttemptRequest(string Heard);

public sealed record AddTermRequest(string Term, string? Respelling, string? Ipa, string? TipEs, string? Accepted);

public sealed record KeyTermInput(string Term, string Respelling, string? Ipa, string? TipEs);

public sealed partial class PronunciationService(AppDbContext db, TimeProvider clock)
{
    public Task<List<PronunciationTerm>> ListAsync(CancellationToken ct) =>
        db.PronunciationTerms.AsNoTracking()
            .OrderByDescending(t => t.Source == "simulator")
            .ThenBy(t => t.Term)
            .ToListAsync(ct);

    public async Task<object> RecordAttemptAsync(int id, AttemptRequest request, CancellationToken ct)
    {
        var term = await db.PronunciationTerms.FindAsync([id], ct) ?? throw new KeyNotFoundException("Término no encontrado.");
        var ok = IsMatch(term, request.Heard);
        term.Attempts++;
        if (ok) term.Successes++;
        term.LastHeard = request.Heard;
        term.LastPracticedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return new { recognized = ok, heard = request.Heard, term };
    }

    public async Task<PronunciationTerm> AddAsync(AddTermRequest r, string source, CancellationToken ct)
    {
        var existing = await FindAsync(r.Term, ct);
        if (existing is not null) return existing;

        var term = new PronunciationTerm
        {
            Term = r.Term.Trim(),
            Respelling = r.Respelling?.Trim() ?? "",
            Ipa = r.Ipa,
            TipEs = r.TipEs,
            Accepted = string.IsNullOrWhiteSpace(r.Accepted) ? r.Term.Trim().ToLowerInvariant() : r.Accepted,
            Source = source,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.PronunciationTerms.Add(term);
        await db.SaveChangesAsync(ct);
        return term;
    }

    /// <summary>Terms the speech recognizer did not catch during a simulated interview.</summary>
    public async Task AddFromSimulatorAsync(IEnumerable<MisrecognizedTerm> terms, CancellationToken ct)
    {
        foreach (var t in terms)
        {
            var existing = await FindAsync(t.Intended, ct);
            if (existing is not null)
            {
                existing.LastHeard = t.Heard;
                if (existing.Source == "seed") existing.Source = "simulator";
                continue;
            }
            db.PronunciationTerms.Add(new PronunciationTerm
            {
                Term = t.Intended.Trim(),
                Accepted = t.Intended.Trim().ToLowerInvariant(),
                LastHeard = t.Heard,
                TipEs = $"El reconocedor escuchó \"{t.Heard}\".",
                Source = "simulator",
                CreatedAt = clock.GetUtcNow().UtcDateTime,
            });
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Key terms that come with generated lessons (respelling + IPA included).</summary>
    public async Task AddFromLessonAsync(IEnumerable<KeyTermInput> terms, CancellationToken ct)
    {
        foreach (var t in terms.Where(t => !string.IsNullOrWhiteSpace(t.Term)))
        {
            var existing = await FindAsync(t.Term, ct);
            if (existing is not null)
            {
                if (string.IsNullOrEmpty(existing.Respelling)) existing.Respelling = t.Respelling;
                existing.Ipa ??= t.Ipa;
                existing.TipEs ??= t.TipEs;
                continue;
            }
            db.PronunciationTerms.Add(new PronunciationTerm
            {
                Term = t.Term.Trim(),
                Respelling = t.Respelling,
                Ipa = t.Ipa,
                TipEs = t.TipEs,
                Accepted = t.Term.Trim().ToLowerInvariant(),
                Source = "lesson",
                CreatedAt = clock.GetUtcNow().UtcDateTime,
            });
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task<PronunciationTerm?> FindAsync(string term, CancellationToken ct)
    {
        var normalized = term.Trim().ToLower();
        return db.PronunciationTerms.Local.FirstOrDefault(t => t.Term.ToLower() == normalized)
               ?? await db.PronunciationTerms.FirstOrDefaultAsync(t => t.Term.ToLower() == normalized, ct);
    }

    /// <summary>
    /// True when the transcript contains the term or one of its accepted spellings as whole words.
    /// Homophones count as correct ("cash" for cache): the recognizer cannot tell them apart and neither can a listener.
    /// </summary>
    public static bool IsMatch(PronunciationTerm term, string heard)
    {
        var transcript = " " + Normalize(heard) + " ";
        var accepted = term.Accepted.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Append(term.Term);
        return accepted.Select(Normalize).Where(a => a.Length > 0).Any(a => transcript.Contains(" " + a + " "));
    }

    internal static string Normalize(string s) =>
        Spaces().Replace(NonWord().Replace(s.ToLowerInvariant().Replace(".", " dot ").Replace("#", " sharp"), " "), " ").Trim();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonWord();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
