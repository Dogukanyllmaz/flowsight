using FlowSight.Core.Journeys;
using FlowSight.Core.Model;

namespace FlowSight.Tests;

internal static class JourneyFixtures
{
    /// <summary>A line journey whose stages up to <paramref name="current"/> (or all, when closed) carry <paramref name="minutesPerStage"/>.</summary>
    public static LineJourney Line(
        string lineId,
        LineStatus status,
        Stage? current,
        double total = 0,
        string caseId = "R1",
        string category = "Spare parts",
        LinePath path = LinePath.Goods,
        IReadOnlyDictionary<Stage, double>? minutesPerStage = null)
    {
        var last = current ?? Stage.Payment;
        var stages = Stages.All.Select(s =>
        {
            var state = s < last ? StageState.Completed
                : s == last ? (status == LineStatus.Open ? StageState.InProgress : StageState.Completed)
                : StageState.NotStarted;
            var minutes = state == StageState.NotStarted ? 0 : minutesPerStage?.GetValueOrDefault(s) ?? 0;
            return new StageSpan(s, state, null, null, minutes, minutes / 540d);
        }).ToList();

        return new LineJourney(
            new RequestLine(caseId, lineId, "Site North", category, path, 1, 100, "EUR"),
            stages, status == LineStatus.Open ? current : null, status, total, total / 540d);
    }
}
