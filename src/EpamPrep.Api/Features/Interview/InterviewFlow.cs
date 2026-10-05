using EpamPrep.Api.Ai;

namespace EpamPrep.Api.Features.Interview;

public enum FlowAction
{
    GoDeeper,
    Clarify,
    NextTopic,
    /// <summary>Quick mode only: same competency, fresh basic question on another subtopic.</summary>
    NewThread,
    End,
}

/// <param name="IsQuick">Quick block (one competency) vs full interview.</param>
/// <param name="Depth">Follow-ups already asked on the current thread (0 = opening question).</param>
/// <param name="TopicMinutes">Minutes spent on the current topic (full mode) or thread (quick mode).</param>
/// <param name="TopicBudgetMinutes">Time slot of the current topic in a full interview.</param>
/// <param name="LastDecisionWasClarify">Prevents clarify loops: at most one clarify in a row.</param>
public sealed record FlowContext(
    bool IsQuick,
    int TopicIndex,
    int TopicCount,
    int Depth,
    double TopicMinutes,
    double TopicBudgetMinutes,
    double TotalMinutes,
    double PlannedMinutes,
    int MaxDepth,
    bool LastDecisionWasClarify);

public sealed record FlowStep(FlowAction Action, string Reason);

/// <summary>
/// Deterministic controller on top of the evaluator's recommendation.
/// The model proposes (go_deeper / clarify / next_topic); time and depth limits decide.
/// </summary>
public static class InterviewFlow
{
    public static FlowStep Decide(FlowContext c, AnswerAssessment a)
    {
        if (c.TotalMinutes >= c.PlannedMinutes)
        {
            return new(FlowAction.End, "time is up");
        }

        var leaveThread =
            a.Decision == "next_topic" ? "evaluator: ceiling reached" :
            c.Depth + 1 >= c.MaxDepth ? "max depth reached" :
            !c.IsQuick && c.TopicMinutes >= c.TopicBudgetMinutes ? "topic time budget used" :
            null;

        if (leaveThread is not null)
        {
            if (c.IsQuick) return new(FlowAction.NewThread, leaveThread);
            return c.TopicIndex + 1 >= c.TopicCount
                ? new(FlowAction.End, leaveThread + "; no topics left")
                : new(FlowAction.NextTopic, leaveThread);
        }

        if (a.Decision == "clarify" && !c.LastDecisionWasClarify)
        {
            return new(FlowAction.Clarify, "evaluator: answer too vague to grade");
        }

        return new(FlowAction.GoDeeper, "evaluator: solid answer");
    }

    /// <summary>Splits the planned time across topics proportionally to their interview weight.</summary>
    public static double TopicBudget(double plannedMinutes, IReadOnlyList<double> weights, int index)
    {
        var total = weights.Sum();
        return total <= 0 ? plannedMinutes / Math.Max(1, weights.Count) : plannedMinutes * weights[index] / total;
    }
}
