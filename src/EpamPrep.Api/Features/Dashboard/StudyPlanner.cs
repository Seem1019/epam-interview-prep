using EpamPrep.Api.Domain;

namespace EpamPrep.Api.Features.Dashboard;

public sealed record CompetencyStatus(
    string Key, string Title, Level Previous, Level Current, Level Target, bool Weak, int? QuizAverage);

public sealed record PlanTask(string Key, string Kind, string? TopicKey, string Title, int Minutes, bool Done);

public sealed record PlanDay(DateOnly Date, string Label, string Focus, IReadOnlyList<PlanTask> Tasks);

/// <summary>
/// Deterministic day-by-day plan, recomputed on every request from the current estimates:
/// weakest areas (React, JavaScript, GenAI) first, then core topics from Intermediate to Advanced,
/// a full 2-hour mock on the second-to-last day, consolidation on the last one.
/// </summary>
public static class StudyPlanner
{
    public static IReadOnlyList<PlanDay> Build(
        DateOnly today,
        DateOnly interviewDay,
        IReadOnlyList<CompetencyStatus> competencies,
        IReadOnlySet<string> doneKeys,
        bool profileComplete,
        bool fullMockDone)
    {
        var days = new List<PlanDay>();
        var studyDays = new List<DateOnly>();
        for (var d = today; d < interviewDay; d = d.AddDays(1)) studyDays.Add(d);
        if (studyDays.Count == 0) return [InterviewDay(interviewDay, doneKeys)];

        var ranked = Rank(competencies);
        var mockDay = studyDays.Count >= 3 ? studyDays[^2] : studyDays[^1];
        var lastDay = studyDays[^1];
        var queue = new Queue<CompetencyStatus>();

        CompetencyStatus Next()
        {
            if (queue.Count == 0)
            {
                foreach (var c in ranked) queue.Enqueue(c);
            }
            return queue.Dequeue();
        }

        foreach (var day in studyDays)
        {
            var tasks = new List<PlanTask>();
            void Add(string kind, string? topic, string title, int minutes)
            {
                var key = $"{day:yyyy-MM-dd}|{kind}|{topic ?? "-"}";
                tasks.Add(new PlanTask(key, kind, topic, title, minutes, doneKeys.Contains(key)));
            }

            if (day == today && !profileComplete)
            {
                Add("profile", null, "Completar el perfil (CV / experiencia) para preguntas personalizadas", 15);
            }

            if (day == mockDay && day != lastDay)
            {
                Add("pronunciation", null, "Calentamiento de pronunciación", 10);
                var mockKey = $"{day:yyyy-MM-dd}|mock|-";
                tasks.Add(new PlanTask(mockKey, "mock", null, "Simulacro completo de 2 horas (feedback al final)", 120,
                    fullMockDone || doneKeys.Contains(mockKey)));
                Add("report", null, "Revisar el reporte: preguntas falladas y respuestas modelo en voz alta", 30);
                Add("reviews", null, "Repasos pendientes (repetición espaciada)", 20);
                days.Add(new PlanDay(day, Label(day, today), "Simulacro completo", tasks));
                continue;
            }

            if (day == lastDay)
            {
                var weakest = ranked.Take(2).ToList();
                Add("reviews", null, "Repasos pendientes (repetición espaciada)", 25);
                foreach (var c in weakest) Add("quick", c.Key, $"Bloque rápido: {c.Title}", 20);
                Add("lesson", "leadership", "Preparar 4–5 historias STAR (conflicto, incidente, mentoring, decisión técnica)", 40);
                Add("pronunciation", null, "Práctica de pronunciación (términos con más fallos)", 15);
                if (day == mockDay)
                {
                    var mockKey = $"{day:yyyy-MM-dd}|mock|-";
                    tasks.Insert(0, new PlanTask(mockKey, "mock", null, "Simulacro completo de 2 horas", 120,
                        fullMockDone || doneKeys.Contains(mockKey)));
                }
                days.Add(new PlanDay(day, Label(day, today), "Consolidar y descansar", tasks));
                continue;
            }

            var a = Next();
            var b = Next();
            var c2 = Next();
            Add("pronunciation", null, "Calentamiento de pronunciación", 10);
            Add("lesson", a.Key, $"Estudiar: {a.Title}", 40);
            Add("quick", a.Key, $"Bloque rápido: {a.Title}", 20);
            Add("lesson", b.Key, $"Estudiar: {b.Title}", 40);
            Add("quiz", b.Key, $"Evaluación: {b.Title}", 20);
            Add("quiz", c2.Key, $"Evaluación: {c2.Title}", 15);
            Add("reviews", null, "Repasos pendientes (repetición espaciada)", 15);
            days.Add(new PlanDay(day, Label(day, today), $"{a.Title} · {b.Title}", tasks));
        }

        days.Add(InterviewDay(interviewDay, doneKeys));
        return days;
    }

    /// <summary>
    /// Highest priority first. The weak group (React, JavaScript, GenAI) always comes first while it is
    /// below target; then the largest gap to target; low quiz averages break ties.
    /// </summary>
    public static IReadOnlyList<CompetencyStatus> Rank(IReadOnlyList<CompetencyStatus> competencies)
    {
        var list = competencies.Where(c => Priority(c) > 0).OrderByDescending(Priority).ToList();
        return list.Count > 0 ? list : competencies;
    }

    public static double Priority(CompetencyStatus c)
    {
        var gap = Math.Max(0, (int)c.Target - (int)c.Current);
        if (gap == 0) return c.QuizAverage is < 70 ? 1 : 0;
        return (c.Weak ? 100 : 0) + gap * 10 + (c.QuizAverage is null ? 3 : (100 - c.QuizAverage.Value) / 10.0);
    }

    private static PlanDay InterviewDay(DateOnly day, IReadOnlySet<string> doneKeys)
    {
        var key = $"{day:yyyy-MM-dd}|warmup|-";
        return new PlanDay(day, "Entrevista", "Entrevista EPAM",
        [
            new PlanTask(key, "warmup", null, "Calentamiento: 5 respuestas modelo en voz alta + términos clave", 20, doneKeys.Contains(key)),
        ]);
    }

    private static string Label(DateOnly day, DateOnly today)
    {
        var name = day.ToString("dddd d MMM", new System.Globalization.CultureInfo("es-ES"));
        return day == today ? $"Hoy · {name}" : char.ToUpper(name[0]) + name[1..];
    }
}

