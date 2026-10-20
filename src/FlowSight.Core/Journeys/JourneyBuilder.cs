using FlowSight.Core.Calendar;
using FlowSight.Core.Model;

namespace FlowSight.Core.Journeys;

/// <summary>Turns an event log into one journey per request line.</summary>
public static class JourneyBuilder
{
    public static IReadOnlyList<LineJourney> Build(EventLog log, IWorkCalendar calendar)
    {
        var eventsByLine = ApplyCutOff(log).Events
            .GroupBy(e => (e.CaseId, e.LineId))
            .ToDictionary(g => g.Key, g => g.ToList());

        return log.Lines
            .Select(line => BuildLine(line, eventsByLine.GetValueOrDefault((line.CaseId, line.LineId)) ?? [], log.AsOf, calendar))
            .ToList();
    }

    /// <summary>Drops events queued after the as-of date and reopens steps completed after it.</summary>
    public static EventLog ApplyCutOff(EventLog log) =>
        log with
        {
            Events = log.Events
                .Where(e => e.QueuedAt <= log.AsOf)
                .Select(e => e with
                {
                    StartedAt = e.StartedAt > log.AsOf ? null : e.StartedAt,
                    CompletedAt = e.CompletedAt > log.AsOf ? null : e.CompletedAt,
                    Outcome = e.CompletedAt > log.AsOf ? null : e.Outcome
                })
                .ToList()
        };

    private static LineJourney BuildLine(RequestLine line, List<StepEvent> events, DateTimeOffset asOf, IWorkCalendar calendar)
    {
        var status = StatusOf(events);
        var terminal = status != LineStatus.Open;
        var reached = events.Count == 0 ? (Stage?)null : events.Max(e => e.Stage);

        var spans = Stages.All.Select(stage =>
        {
            var inStage = events.Where(e => e.Stage == stage).ToList();
            if (inStage.Count == 0) return new StageSpan(stage, StageState.NotStarted, null, null, 0, 0);

            var open = inStage.Any(e => e.CompletedAt is null);
            var completed = !open && (terminal || stage < reached);
            var intervals = Merge(inStage.Select(e => (e.QueuedAt, e.CompletedAt ?? asOf)));

            return new StageSpan(
                stage,
                completed ? StageState.Completed : StageState.InProgress,
                inStage.Min(e => e.QueuedAt),
                completed ? inStage.Max(e => e.CompletedAt) : null,
                intervals.Sum(i => calendar.WorkingMinutes(i.Start, i.End, line.SiteId)),
                intervals.Sum(i => (i.End - i.Start).TotalDays));
        }).ToList();

        Stage? current = status == LineStatus.Open
            ? events.Where(e => e.CompletedAt is null).Select(e => (Stage?)e.Stage).Max() ?? reached
            : null;

        double totalMinutes = 0, totalDays = 0;
        if (events.Count > 0)
        {
            var start = events.Min(e => e.QueuedAt);
            var end = terminal ? events.Max(e => e.CompletedAt) ?? asOf : asOf;
            totalMinutes = calendar.WorkingMinutes(start, end, line.SiteId);
            totalDays = Math.Max(0, (end - start).TotalDays);
        }

        return new LineJourney(line, spans, current, status, totalMinutes, totalDays);
    }

    private static LineStatus StatusOf(List<StepEvent> events)
    {
        if (events.Any(e => e.Outcome == StepOutcome.Rejected)) return LineStatus.Rejected;
        if (events.Any(e => e.Outcome == StepOutcome.Cancelled)) return LineStatus.Cancelled;
        if (events.Any(e => e.Stage == Stage.Payment && e.Outcome == StepOutcome.Done && e.CompletedAt is not null))
            return LineStatus.Completed;
        return LineStatus.Open;
    }

    /// <summary>Merges overlapping intervals so repeated or parallel steps are not counted twice.</summary>
    private static List<(DateTimeOffset Start, DateTimeOffset End)> Merge(IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> intervals)
    {
        var merged = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        foreach (var (start, end) in intervals.Where(i => i.End > i.Start).OrderBy(i => i.Start))
        {
            if (merged.Count > 0 && start <= merged[^1].End)
                merged[^1] = (merged[^1].Start, end > merged[^1].End ? end : merged[^1].End);
            else
                merged.Add((start, end));
        }
        return merged;
    }
}
