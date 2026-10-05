using EpamPrep.Api.Domain;
using EpamPrep.Api.Features.Dashboard;

namespace EpamPrep.Tests;

public class StudyPlannerTests
{
    private static readonly DateOnly Wednesday = new(2026, 9, 30);
    private static readonly DateOnly Monday = new(2026, 10, 5);

    private static List<CompetencyStatus> FromCatalog() =>
        Catalog.All.Select(c => new CompetencyStatus(c.Key, c.Title, c.Previous, c.Previous, c.Target, c.Weak, null)).ToList();

    [Fact]
    public void WeakGroup_IsStudiedFirst()
    {
        var ranked = StudyPlanner.Rank(FromCatalog());
        Assert.Equal(["genai", "javascript", "react"], ranked.Take(3).Select(c => c.Key).Order());
    }

    [Fact]
    public void Plan_CoversEveryDay_WithFullMockOnSaturday_AndInterviewDayLast()
    {
        var plan = StudyPlanner.Build(Wednesday, Monday, FromCatalog(), new HashSet<string>(), profileComplete: false, fullMockDone: false);

        Assert.Equal(6, plan.Count); // Wed..Sun + Monday warm-up
        Assert.Contains(plan.Single(d => d.Date == new DateOnly(2026, 10, 3)).Tasks, t => t.Kind == "mock" && t.Minutes == 120);
        Assert.Contains(plan[0].Tasks, t => t.Kind == "profile");
        Assert.Contains(plan[0].Tasks, t => t.TopicKey is "javascript" or "react" or "genai");
        Assert.Equal(Monday, plan[^1].Date);
    }

    [Fact]
    public void Plan_IsRecalculated_WhenAWeakTopicReachesTarget()
    {
        var statuses = FromCatalog()
            .Select(c => c.Key is "javascript" or "react" or "genai" ? c with { Current = c.Target, QuizAverage = 90 } : c)
            .ToList();

        var ranked = StudyPlanner.Rank(statuses);

        Assert.DoesNotContain(ranked, c => c.Weak);
    }

    [Fact]
    public void DoneTasks_StayDone_ByStableKey()
    {
        var first = StudyPlanner.Build(Wednesday, Monday, FromCatalog(), new HashSet<string>(), true, false);
        var key = first[0].Tasks[0].Key;

        var second = StudyPlanner.Build(Wednesday, Monday, FromCatalog(), new HashSet<string> { key }, true, false);

        Assert.True(second[0].Tasks.Single(t => t.Key == key).Done);
    }
}
