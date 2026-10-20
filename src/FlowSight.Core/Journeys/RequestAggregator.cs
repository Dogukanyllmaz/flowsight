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

    private static RequestJourney BuildRequest(string caseId, List<LineJourney> lines)
    {
        var open = lines.Where(l => l.Status == LineStatus.Open).ToList();
        var active = lines.Where(l => l.Status is LineStatus.Open or LineStatus.Completed).ToList();
        var considered = active.Count > 0 ? active : lines;

        var stages = Stages.All.Select(stage =>
        {
            var spans = considered.Select(l => l.Stages.Single(s => s.Stage == stage)).ToList();
            var state =
                spans.All(s => s.State == StageState.Completed) ? StageState.Completed
                : spans.Any(s => s.State != StageState.NotStarted) ? StageState.InProgress
                : StageState.NotStarted;

            return new StageSpan(
                stage,
                state,
                spans.Min(s => s.EnteredAt),
                state == StageState.Completed ? spans.Max(s => s.LeftAt) : null,
                spans.Max(s => s.WorkingMinutes),
                spans.Max(s => s.CalendarDays));
        }).ToList();

        return new RequestJourney(
            caseId,
            lines,
            stages,
            open.Select(l => l.CurrentStage).Where(s => s is not null).Min(),
            StatusOf(lines),
            lines.Max(l => l.TotalWorkingMinutes));
    }

    private static RequestStatus StatusOf(List<LineJourney> lines)
    {
        if (lines.Any(l => l.Status == LineStatus.Open)) return RequestStatus.Open;
        if (lines.Any(l => l.Status == LineStatus.Completed)) return RequestStatus.Completed;
        if (lines.Any(l => l.Status == LineStatus.Rejected)) return RequestStatus.Rejected;
        return RequestStatus.Cancelled;
    }
}
