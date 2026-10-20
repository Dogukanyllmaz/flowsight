using FlowSight.Core.Calendar;
using FlowSight.Core.Journeys;
using FlowSight.Core.Model;

namespace FlowSight.Core.Statistics;

/// <summary>Work waiting in a stage at period end, by working days already spent there: ≤ 2, 3–5, 6–10, &gt; 10.</summary>
public sealed record AgeingBuckets(int UpTo2, int From3To5, int From6To10, int Over10);

/// <param name="Throughput">Line spans that left the stage inside the period.</param>
/// <param name="MedianMinutes">Median working minutes of those spans.</param>
/// <param name="MedianInclOpenMinutes">Kaplan–Meier median over spans that entered in the period, including those still waiting.</param>
/// <param name="ShareOfLeadTime">Share (0–1) of total lead time spent in this stage by lines paid in the period.</param>
public sealed record StageStats(
    Stage Stage,
    int Throughput,
    int WipStart,
    int WipEnd,
    double? MedianMinutes,
    double? P90Minutes,
    double? MedianInclOpenMinutes,
    double ShareOfLeadTime,
    AgeingBuckets Ageing);

public static class StageStatistics
{
    public static IReadOnlyList<StageStats> Compute(IReadOnlyList<LineJourney> lines, Period period, IWorkCalendar calendar)
    {
        var paid = lines
            .Where(l => l.Status == LineStatus.Completed && In(Span(l, Stage.Payment).LeftAt, period))
            .ToList();
        var paidTotal = paid.Sum(l => l.TotalWorkingMinutes);

        return Stages.All.Select(stage =>
        {
            var spans = lines.Select(l => (Line: l, Span: Span(l, stage))).Where(x => x.Span.EnteredAt is not null).ToList();

            var left = spans.Where(x => x.Span.State == StageState.Completed && In(x.Span.LeftAt, period))
                .Select(x => x.Span.WorkingMinutes).ToList();

            var entered = spans.Where(x => In(x.Span.EnteredAt, period)).Select(x =>
                x.Span.LeftAt is { } exit && exit <= period.End
                    ? (x.Span.WorkingMinutes, true)
                    : (calendar.WorkingMinutes(x.Span.EnteredAt!.Value, period.End, x.Line.Line.SiteId), false)).ToList();

            var waitingAtEnd = spans.Where(x => WaitingAt(x.Span, period.End)).ToList();
            var ages = waitingAtEnd
                .Select(x => Math.Floor(calendar.WorkingMinutes(x.Span.EnteredAt!.Value, period.End, x.Line.Line.SiteId) / calendar.MinutesPerDay))
                .ToList();

            return new StageStats(
                stage,
                left.Count,
                spans.Count(x => WaitingAt(x.Span, period.Start)),
                waitingAtEnd.Count,
                Percentiles.Median(left),
                Percentiles.Percentile(left, 0.9),
                Percentiles.KaplanMeierMedian(entered),
                paidTotal > 0 ? paid.Sum(l => Span(l, stage).WorkingMinutes) / paidTotal : 0,
                new AgeingBuckets(
                    ages.Count(d => d <= 2),
                    ages.Count(d => d is > 2 and <= 5),
                    ages.Count(d => d is > 5 and <= 10),
                    ages.Count(d => d > 10)));
        }).ToList();
    }

    /// <summary>In the stage at instant <paramref name="t"/>: entered by then and not yet left.
    /// Rejected and cancelled lines have left the stage they ended in, so they drop out naturally.</summary>
    private static bool WaitingAt(StageSpan span, DateTimeOffset t) =>
        span.EnteredAt <= t && (span.LeftAt is null || span.LeftAt > t);

    private static bool In(DateTimeOffset? t, Period period) => t is { } v && v >= period.Start && v < period.End;

    private static StageSpan Span(LineJourney line, Stage stage) => line.Stages[(int)stage - 1];
}
