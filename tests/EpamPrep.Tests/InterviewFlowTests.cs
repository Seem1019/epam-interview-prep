using EpamPrep.Api.Ai;
using EpamPrep.Api.Domain;
using EpamPrep.Api.Features.Interview;

namespace EpamPrep.Tests;

public class InterviewFlowTests
{
    private static AnswerAssessment Assessment(string decision, int score = 7) =>
        new(Level.Intermediate, score, "", decision, "", "", new Concision("acceptable", "", ""), "", []);

    private static FlowContext Full(int topicIndex = 0, int depth = 1, double topicMinutes = 2, double total = 10, bool lastClarify = false) =>
        new(IsQuick: false, topicIndex, TopicCount: 14, depth, topicMinutes, TopicBudgetMinutes: 9, total, PlannedMinutes: 120, MaxDepth: 6, lastClarify);

    [Fact]
    public void SolidAnswer_GoesDeeper() =>
        Assert.Equal(FlowAction.GoDeeper, InterviewFlow.Decide(Full(), Assessment("go_deeper")).Action);

    [Fact]
    public void CeilingReached_MovesToNextTopic() =>
        Assert.Equal(FlowAction.NextTopic, InterviewFlow.Decide(Full(), Assessment("next_topic", 3)).Action);

    [Fact]
    public void QuickMode_StartsNewThreadInsteadOfChangingTopic()
    {
        var quick = Full() with { IsQuick = true, TopicCount = 1 };
        Assert.Equal(FlowAction.NewThread, InterviewFlow.Decide(quick, Assessment("next_topic", 2)).Action);
    }

    [Fact]
    public void TopicBudgetExceeded_MovesOn_EvenIfEvaluatorWantsMore() =>
        Assert.Equal(FlowAction.NextTopic, InterviewFlow.Decide(Full(topicMinutes: 9.5), Assessment("go_deeper")).Action);

    [Fact]
    public void MaxDepth_MovesOn() =>
        Assert.Equal(FlowAction.NextTopic, InterviewFlow.Decide(Full(depth: 5), Assessment("go_deeper", 9)).Action);

    [Fact]
    public void Clarify_IsAllowedOnlyOnceInARow()
    {
        Assert.Equal(FlowAction.Clarify, InterviewFlow.Decide(Full(), Assessment("clarify", 5)).Action);
        Assert.Equal(FlowAction.GoDeeper, InterviewFlow.Decide(Full(lastClarify: true), Assessment("clarify", 5)).Action);
    }

    [Fact]
    public void TimeUp_EndsInterview() =>
        Assert.Equal(FlowAction.End, InterviewFlow.Decide(Full(total: 121), Assessment("go_deeper")).Action);

    [Fact]
    public void LastTopicFinished_EndsInterview() =>
        Assert.Equal(FlowAction.End, InterviewFlow.Decide(Full(topicIndex: 13), Assessment("next_topic", 2)).Action);

    [Fact]
    public void TopicBudget_IsProportionalToWeight()
    {
        double[] weights = [1.0, 2.0, 1.0];
        Assert.Equal(60, InterviewFlow.TopicBudget(120, weights, 1));
        Assert.Equal(30, InterviewFlow.TopicBudget(120, weights, 0));
    }
}
