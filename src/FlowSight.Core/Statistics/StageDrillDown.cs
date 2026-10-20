using FlowSight.Core.Calendar;
using FlowSight.Core.Journeys;
using FlowSight.Core.Model;

namespace FlowSight.Core.Statistics;

/// <summary>Time spent by one role or one person on a stage's steps.</summary>
/// <param name="Key">Role name, or pseudonymous actor id.</param>
public sealed record GroupTime(string Key, string Role, int Steps, double? MedianMinutes, double TotalMinutes);

public sealed record SlowCase(string CaseId, string LineId, string ItemCategory, double WorkingMinutes);

/// <param name="MedianQueueMinutes">Waiting before someone picked the step up; steps without a start time are left out.</param>
/// <param name="MedianWorkMinutes">From pick-up to completion; steps without a start time are left out.</param>
public sealed record StageDetail(
    Stage Stage,
    string PeriodId,
    double? MedianQueueMinutes,
    double? MedianWorkMinutes,
    IReadOnlyList<GroupTime> ByRole,
    IReadOnlyList<GroupTime> ByPerson,
    IReadOnlyDictionary<AssignmentKind, int> AssignmentMix,
    AgeingBuckets Ageing,
    IReadOnlyList<SlowCase> SlowestOpen,
    IReadOnlyList<SlowCase> SlowestCompleted);

public static class StageDrillDown
{
    public const int SlowListSize = 10;

    public static StageDetail Compute(EventLog log, IReadOnlyList<LineJourney> lines, Stage stage, Period period, IWorkCalendar calendar)
    {
        var siteOf = log.Lines.ToDictionary(l => (l.CaseId, l.LineId), l => l.SiteId);
        var steps = JourneyBuilder.ApplyCutOff(log).Events
            .Where(e => e.Stage == stage && e.CompletedAt is { } done && done >= period.Start && done < period.End)
            .Select(e =>
            {
                var site = siteOf.GetValueOrDefault((e.CaseId, e.LineId));
                double? queue = e.StartedAt is { } s ? calendar.WorkingMinutes(e.QueuedAt, s, site) : null;
                double? work = e.StartedAt is { } s2 ? calendar.WorkingMinutes(s2, e.CompletedAt!.Value, site) : null;
                return (Event: e, Queue: queue, Work: work, Total: calendar.WorkingMinutes(e.QueuedAt, e.CompletedAt!.Value, site));
            })
            .ToList();

        IReadOnlyList<GroupTime> Group(Func<StepEvent, string> key) => steps
            .GroupBy(x => key(x.Event))
            .Select(g => new GroupTime(g.Key, g.First().Event.Role, g.Count(),
                Percentiles.Median(g.Select(x => x.Total).ToList()), g.Sum(x => x.Total)))
            .OrderByDescending(g => g.TotalMinutes)
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        var spans = lines.Select(l => (Line: l, Span: l.Stages[(int)stage - 1])).ToList();

        var slowestOpen = spans
            .Where(x => x.Span.EnteredAt <= period.End && (x.Span.LeftAt is null || x.Span.LeftAt > period.End))
            .Select(x => Slow(x.Line, calendar.WorkingMinutes(x.Span.EnteredAt!.Value, period.End, x.Line.Line.SiteId)))
            .OrderByDescending(c => c.WorkingMinutes).ThenBy(c => c.CaseId, StringComparer.Ordinal)
            .Take(SlowListSize).ToList();

        var slowestCompleted = spans
            .Where(x => x.Span.State == StageState.Completed && x.Span.LeftAt >= period.Start && x.Span.LeftAt < period.End)
            .Select(x => Slow(x.Line, x.Span.WorkingMinutes))
            .OrderByDescending(c => c.WorkingMinutes).ThenBy(c => c.CaseId, StringComparer.Ordinal)
            .Take(SlowListSize).ToList();

        return new StageDetail(
            stage,
            period.Id,
            Percentiles.Median(steps.Where(x => x.Queue is not null).Select(x => x.Queue!.Value).ToList()),
            Percentiles.Median(steps.Where(x => x.Work is not null).Select(x => x.Work!.Value).ToList()),
            Group(e => e.Role),
            Group(e => e.ActorId),
            steps.GroupBy(x => x.Event.Assignment).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
            StageStatistics.Compute(lines, period, calendar)[(int)stage - 1].Ageing,
            slowestOpen,
            slowestCompleted);
    }

    private static SlowCase Slow(LineJourney line, double minutes) =>
        new(line.Line.CaseId, line.Line.LineId, line.Line.ItemCategory, minutes);
}
