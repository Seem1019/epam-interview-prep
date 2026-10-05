using EpamPrep.Api.Data;

namespace EpamPrep.Api.Features.Quiz;

public static class QuizScoring
{
    public const int PassScore = 70;

    /// <summary>Maps a 0–100 score to SM-2 quality (0–5).</summary>
    public static int ToQuality(int score) => score switch
    {
        >= 90 => 5,
        >= 80 => 4,
        >= PassScore => 3,
        >= 50 => 2,
        >= 25 => 1,
        _ => 0,
    };
}

/// <summary>
/// SM-2 variant compressed for a 4-day horizon:
/// a failure comes back in 10 minutes (same session), the first success in 1 day, the second in 3 days,
/// then the interval grows by the ease factor. Ease is updated with the classic SM-2 formula, floor 1.3.
/// </summary>
public static class SpacedRepetition
{
    public const double RelearnMinutes = 10;
    public const double FirstIntervalMinutes = 24 * 60;
    public const double SecondIntervalMinutes = 3 * 24 * 60;
    public const double MinEase = 1.3;

    public static void Apply(ReviewItem item, int quality, DateTime now)
    {
        quality = Math.Clamp(quality, 0, 5);

        if (quality < 3)
        {
            item.Repetitions = 0;
            item.IntervalMinutes = RelearnMinutes;
            item.Lapses++;
        }
        else
        {
            item.Repetitions++;
            item.IntervalMinutes = item.Repetitions switch
            {
                1 => FirstIntervalMinutes,
                2 => SecondIntervalMinutes,
                _ => Math.Round(item.IntervalMinutes * item.EaseFactor),
            };
        }

        // SM-2: EF' = EF + (0.1 - (5 - q) * (0.08 + (5 - q) * 0.02))
        var ease = item.EaseFactor + (0.1 - (5 - quality) * (0.08 + (5 - quality) * 0.02));
        item.EaseFactor = Math.Max(MinEase, Math.Round(ease, 2));

        item.LastReviewedAt = now;
        item.DueAt = now.AddMinutes(item.IntervalMinutes);
    }

    /// <summary>Creates the review item the first time a question is failed.</summary>
    public static ReviewItem StartAfterFailure(int questionId, int quality, DateTime now)
    {
        var item = new ReviewItem { QuestionId = questionId, EaseFactor = 2.5 };
        Apply(item, quality, now);
        return item;
    }
}
