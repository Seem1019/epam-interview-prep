using EpamPrep.Api.Domain;

namespace EpamPrep.Api.Features.Dashboard;

public static class LevelEstimator
{
    public const int MinAttemptsPerLevel = 2;
    public const int PassAverage = 70;

    /// <summary>
    /// Quiz-based estimate: climb the scale while each level with enough attempts averages ≥ 70.
    /// A level without enough attempts neither raises nor blocks the estimate; a failed level stops the climb.
    /// </summary>
    public static Level FromQuiz(IEnumerable<(Level QuestionLevel, int Score)> attempts)
    {
        var byLevel = attempts
            .GroupBy(a => a.QuestionLevel)
            .ToDictionary(g => g.Key, g => g.TakeLast(10).Select(a => a.Score).ToList());

        var estimate = Level.None;
        foreach (var level in new[] { Level.Novice, Level.Intermediate, Level.Advanced, Level.Expert })
        {
            if (!byLevel.TryGetValue(level, out var scores) || scores.Count < MinAttemptsPerLevel) continue;
            if (scores.Average() >= PassAverage) estimate = level;
            else break;
        }
        return estimate;
    }
}
