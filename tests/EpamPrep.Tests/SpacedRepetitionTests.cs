using EpamPrep.Api.Data;
using EpamPrep.Api.Features.Quiz;

namespace EpamPrep.Tests;

public class SpacedRepetitionTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Failure_ComesBackInTenMinutes_AndLowersEase()
    {
        var item = SpacedRepetition.StartAfterFailure(questionId: 1, quality: 1, Now);

        Assert.Equal(0, item.Repetitions);
        Assert.Equal(1, item.Lapses);
        Assert.Equal(Now.AddMinutes(10), item.DueAt);
        Assert.True(item.EaseFactor < 2.5);
    }

    [Fact]
    public void Successes_FollowOneDay_ThreeDays_ThenEaseMultiplier()
    {
        var item = new ReviewItem { EaseFactor = 2.5 };

        SpacedRepetition.Apply(item, quality: 4, Now);
        Assert.Equal(TimeSpan.FromDays(1), item.DueAt - Now);

        SpacedRepetition.Apply(item, quality: 4, Now);
        Assert.Equal(TimeSpan.FromDays(3), item.DueAt - Now);

        var ease = item.EaseFactor;
        SpacedRepetition.Apply(item, quality: 4, Now);
        Assert.Equal(Math.Round(3 * 24 * 60 * ease), item.IntervalMinutes);
        Assert.Equal(3, item.Repetitions);
    }

    [Fact]
    public void Lapse_AfterProgress_ResetsRepetitions()
    {
        var item = new ReviewItem { EaseFactor = 2.5 };
        SpacedRepetition.Apply(item, 5, Now);
        SpacedRepetition.Apply(item, 5, Now);

        SpacedRepetition.Apply(item, 2, Now);

        Assert.Equal(0, item.Repetitions);
        Assert.Equal(SpacedRepetition.RelearnMinutes, item.IntervalMinutes);
    }

    [Fact]
    public void EaseFactor_NeverDropsBelowFloor()
    {
        var item = new ReviewItem { EaseFactor = 1.35 };
        for (var i = 0; i < 10; i++) SpacedRepetition.Apply(item, 0, Now);
        Assert.Equal(SpacedRepetition.MinEase, item.EaseFactor);
    }

    [Fact]
    public void PerfectAnswer_IncreasesEase()
    {
        var item = new ReviewItem { EaseFactor = 2.5 };
        SpacedRepetition.Apply(item, 5, Now);
        Assert.Equal(2.6, item.EaseFactor);
    }

    [Theory]
    [InlineData(100, 5)]
    [InlineData(85, 4)]
    [InlineData(70, 3)]
    [InlineData(69, 2)]
    [InlineData(30, 1)]
    [InlineData(0, 0)]
    public void ScoreMapsToQuality_PassMarkIsSeventy(int score, int quality) =>
        Assert.Equal(quality, QuizScoring.ToQuality(score));
}
