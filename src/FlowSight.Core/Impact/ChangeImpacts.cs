using FlowSight.Core.Calendar;
using FlowSight.Core.Journeys;
using FlowSight.Core.Model;
using FlowSight.Core.ProcessMaps;
using FlowSight.Core.Statistics;

namespace FlowSight.Core.Impact;

/// <summary>
/// Measures every dated change in the process map: the changed step over equal-length windows before and after the
/// change, and a rule-based verdict.
/// </summary>
public static class ChangeImpacts
{
    public const int MinimumWindowDays = 14;
    public const int MinimumSteps = 20;
    public const double ImprovedRatio = 0.8;
    public const double WorseRatio = 1.2;
    private static readonly TimeOnly Noon = new(12, 0);

    public static IReadOnlyList<ChangeImpact> Measure(EventLog log, ProcessMap map, IWorkCalendar calendar, int maxWindowDays = 90)
    {
        if (map.Changes.Count == 0) return [];

        var cut = JourneyBuilder.ApplyCutOff(log);
        var offset = log.AsOf.Offset;
        var firstEvent = cut.Events.Count == 0 ? log.AsOf : cut.Events.Min(e => e.QueuedAt);
        var siteOf = log.Lines.ToDictionary(l => (l.CaseId, l.LineId), l => l.SiteId);
        var byActivity = cut.Events.ToLookup(e => e.Activity);
        var months = Periods.For(log).Where(p => p.Id != Periods.Last12Id).ToList();

        DateTimeOffset At(DateOnly day) => new(day.ToDateTime(TimeOnly.MinValue), offset);

        return map.Changes.Select(change =>
        {
            var step = map.Steps.Single(s => s.Id == change.Step);
            var instant = At(change.Date);
            var sameStep = map.Changes.Where(c => c.Step == change.Step && c != change).Select(c => At(c.Date)).ToList();

            // Equal windows that stay inside the data and never cross a neighbouring change on the same step.
            var limits = new List<double> { maxWindowDays, (instant - firstEvent).TotalDays, (log.AsOf - instant).TotalDays };
            limits.AddRange(sameStep.Where(t => t < instant).Select(t => (instant - t).TotalDays));
            limits.AddRange(sameStep.Where(t => t > instant).Select(t => (t - instant).TotalDays));
            var days = Math.Max(0, (int)Math.Floor(limits.Min()));

            var before = new ImpactWindow(instant.AddDays(-days), instant);
            var after = new ImpactWindow(instant, instant.AddDays(days));
            var events = byActivity[step.Name].ToList();

            ImpactStats Stats(ImpactWindow w) => Measure(events, w, calendar, siteOf);
            var beforeStats = Stats(before);
            var afterStats = Stats(after);

            double? percent = beforeStats.MedianStepMinutes is > 0 && afterStats.MedianStepMinutes is { } a
                ? Math.Round((a / beforeStats.MedianStepMinutes.Value - 1) * 100, 1)
                : null;

            var verdict =
                days < MinimumWindowDays || beforeStats.Steps < MinimumSteps || afterStats.Steps < MinimumSteps || percent is null
                    ? ImpactVerdict.InsufficientData
                    : percent <= (ImprovedRatio - 1) * 100 ? ImpactVerdict.Improved
                    : percent >= (WorseRatio - 1) * 100 ? ImpactVerdict.Worse
                    : ImpactVerdict.Unchanged;

            var monthly = months
                .Where(m => m.Start < after.End && m.End > before.Start)
                .Select(m =>
                {
                    var done = Completed(events, m.Start, m.End).Select(e => Minutes(e, e.QueuedAt, e.CompletedAt!.Value, calendar, siteOf)).ToList();
                    return new MonthlyPoint(m.Id, Percentiles.Median(done), done.Count);
                })
                .ToList();

            return new ChangeImpact(change, step.Id, step.Name, step.Stage, before, after, beforeStats, afterStats, percent, verdict, monthly);
        }).ToList();
    }

    private static ImpactStats Measure(List<StepEvent> events, ImpactWindow window, IWorkCalendar calendar,
        IReadOnlyDictionary<(string, string), string> siteOf)
    {
        var done = Completed(events, window.Start, window.End).ToList();
        var totals = done.Select(e => Minutes(e, e.QueuedAt, e.CompletedAt!.Value, calendar, siteOf)).ToList();
        var queues = done.Where(e => e.StartedAt is not null).Select(e => Minutes(e, e.QueuedAt, e.StartedAt!.Value, calendar, siteOf)).ToList();

        // Items of this step open at noon on each working day of the window.
        var counts = new List<int>();
        for (var day = DateOnly.FromDateTime(window.Start.DateTime); At(day, window.Start.Offset) < window.End; day = day.AddDays(1))
        {
            var noon = new DateTimeOffset(day.ToDateTime(Noon), window.Start.Offset);
            if (noon < window.Start || noon >= window.End || calendar.WorkingMinutes(noon, noon.AddMinutes(1)) < 1) continue;
            counts.Add(events.Count(e => e.QueuedAt <= noon && (e.CompletedAt is null || e.CompletedAt > noon)));
        }

        return new ImpactStats(
            done.Count,
            Percentiles.Median(totals),
            Percentiles.Percentile(totals, 0.9),
            Percentiles.Median(queues),
            counts.Count == 0 ? 0 : Math.Round(counts.Average(), 1));
    }

    private static DateTimeOffset At(DateOnly day, TimeSpan offset) => new(day.ToDateTime(TimeOnly.MinValue), offset);

    private static IEnumerable<StepEvent> Completed(IEnumerable<StepEvent> events, DateTimeOffset start, DateTimeOffset end) =>
        events.Where(e => e.CompletedAt is { } c && c >= start && c < end);

    private static double Minutes(StepEvent e, DateTimeOffset from, DateTimeOffset to, IWorkCalendar calendar,
        IReadOnlyDictionary<(string, string), string> siteOf) =>
        calendar.WorkingMinutes(from, to, siteOf.GetValueOrDefault((e.CaseId, e.LineId)));
}
