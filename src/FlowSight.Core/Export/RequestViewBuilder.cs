using FlowSight.Core.Journeys;
using static FlowSight.Core.Export.JsonDefaults;

namespace FlowSight.Core.Export;

public static class RequestViewBuilder
{
    /// <param name="allLines">Lines to compare against; only completed lines with the same path and category are used.</param>
    public static RequestView Build(RequestJourney request, IReadOnlyList<LineJourney> allLines, int minutesPerDay, DateTimeOffset asOf)
    {
        double Days(double minutes) => Math.Round(minutes / minutesPerDay, 1);
        double? MaybeDays(double? minutes) => minutes is null ? null : Days(minutes.Value);

        StageView View(StageSpan span, LineJourney comparedLine) => new(
            Name(span.Stage),
            Name(span.State),
            span.EnteredAt,
            span.LeftAt,
            Days(span.WorkingMinutes),
            Math.Round(span.CalendarDays, 1),
            MaybeDays(Similarity.TypicalStageMinutes(span.Stage, comparedLine, allLines)));

        // The request is compared through its slowest open line, or its slowest line once closed.
        var representative = request.Lines
            .OrderByDescending(l => l.Status == LineStatus.Open)
            .ThenByDescending(l => l.TotalWorkingMinutes)
            .First();

        // Only open and paid requests are comparable with completed journeys; "slower than 0 %" says nothing.
        var slowerThan = request.Status is RequestStatus.Open or RequestStatus.Completed
            ? Similarity.SlowerThanPercent(representative, allLines)
            : null;
        if (slowerThan == 0) slowerThan = null;

        return new RequestView(
            request.CaseId,
            Name(request.Status),
            request.CurrentStage is { } current ? Name(current) : null,
            Days(request.TotalWorkingMinutes),
            slowerThan,
            request.Stages.Select(s => View(s, RequestAggregator.DefiningLine(request.Lines, s.Stage))).ToList(),
            request.Lines.Select(line => new LineView(
                line.Line.LineId,
                line.Line.ItemCategory,
                Name(line.Line.Path),
                Name(line.Status),
                line.CurrentStage is { } c ? Name(c) : null,
                line.Stages.Select(s => View(s, line)).ToList())).ToList(),
            asOf);
    }
}
