using FlowSight.Core.Model;

namespace FlowSight.Core.Journeys;

/// <summary>Rolls line journeys up to request journeys.</summary>
public static class RequestAggregator
{
    public static IReadOnlyList<RequestJourney> Build(IReadOnlyList<LineJourney> lines) =>
        lines
            .GroupBy(l => l.Line.CaseId)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => BuildRequest(g.Key, g.ToList()))
            .ToList();

    private static RequestJourney BuildRequest(string caseId, IReadOnlyList<LineJourney> lines)
    {
        var open = lines.Where(l => l.Status == LineStatus.Open).ToList();
        var considered = Considered(lines);

        var stages = Stages.All.Select(stage =>
        {
            var spans = considered.Select(l => l.Stages.Single(s => s.Stage == stage)).ToList();
            var state =
                spans.All(s => s.State == StageState.Completed) ? StageState.Completed
                : spans.Any(s => s.State != StageState.NotStarted) ? StageState.InProgress
                : StageState.NotStarted;

            var defining = DefiningLine(lines, stage).Stages.Single(s => s.Stage == stage);
            return new StageSpan(
                stage,
                state,
                spans.Min(s => s.EnteredAt),
                state == StageState.Completed ? spans.Max(s => s.LeftAt) : null,
                defining.WorkingMinutes,
                defining.CalendarDays);
        }).ToList();

        return new RequestJourney(
            caseId,
            lines,
            stages,
            open.Select(l => l.CurrentStage).Where(s => s is not null).Min(),
            StatusOf(lines),
            lines.Max(l => l.TotalWorkingMinutes));
    }

    /// <summary>
    /// The line whose time represents the request in <paramref name="stage"/>: while some line is still in the stage,
    /// the longest-waiting of those; otherwise the line that spent longest in it.
    /// Rejected and cancelled lines only count when no line is still active.
    /// </summary>
    public static LineJourney DefiningLine(IReadOnlyList<LineJourney> lines, Stage stage)
    {
        StageSpan Span(LineJourney l) => l.Stages.Single(s => s.Stage == stage);
        var considered = Considered(lines);
        var stillThere = considered.Where(l => Span(l).State == StageState.InProgress).ToList();
        return (stillThere.Count > 0 ? stillThere : considered).MaxBy(l => Span(l).WorkingMinutes)!;
    }

    private static IReadOnlyList<LineJourney> Considered(IReadOnlyList<LineJourney> lines)
    {
        var active = lines.Where(l => l.Status is LineStatus.Open or LineStatus.Completed).ToList();
        return active.Count > 0 ? active : lines;
    }

    private static RequestStatus StatusOf(IReadOnlyList<LineJourney> lines)
    {
        if (lines.Any(l => l.Status == LineStatus.Open)) return RequestStatus.Open;
        if (lines.Any(l => l.Status == LineStatus.Completed)) return RequestStatus.Completed;
        if (lines.Any(l => l.Status == LineStatus.Rejected)) return RequestStatus.Rejected;
        return RequestStatus.Cancelled;
    }
}
