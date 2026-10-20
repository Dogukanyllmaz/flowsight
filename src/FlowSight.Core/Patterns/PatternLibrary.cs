using FlowSight.Core.Calendar;
using FlowSight.Core.Journeys;
using FlowSight.Core.Model;
using FlowSight.Core.ProcessMaps;
using FlowSight.Core.Statistics;

namespace FlowSight.Core.Patterns;

/// <summary>Runs every pattern detector for one period against the map as it was at the end of that period.</summary>
public static class PatternLibrary
{
    /// <summary>A step needs at least this many completed events in the period before any pattern is judged.</summary>
    public const int MinimumEvents = 20;

    public static PatternResult Detect(EventLog log, IReadOnlyList<LineJourney> lines, ProcessMap map, Period period, IWorkCalendar calendar)
    {
        // Period.End is exclusive: judge the period with the map as it was on its last day.
        var context = new DetectionContext(JourneyBuilder.ApplyCutOff(log), lines, map.AsOf(period.End.AddTicks(-1)), period, calendar);
        var cards = new List<ImprovementCard>();
        var insufficient = new List<string>();

        foreach (var step in context.Map.Steps)
        {
            var events = context.CompletedIn(step);
            if (events.Count < MinimumEvents) continue;

            Detectors.SharedQueueSearch(context, step, events, cards, insufficient);
            Detectors.SinglePointOfFailure(context, step, events, cards, insufficient);
            Detectors.ReworkLoop(context, step, events, cards);
            Detectors.BatchWaiting(context, step, events, cards);
            Detectors.ExceptionQueue(context, step, events, cards, insufficient);
        }
        foreach (var chain in context.Map.ApprovalChains(3))
            Detectors.SerialApprovalStack(context, chain, cards);

        var mapped = context.Map.Steps.Select(s => s.Name).ToHashSet();
        var unmapped = context.PeriodEvents.Select(e => e.Activity).Where(a => !mapped.Contains(a)).Distinct().Order(StringComparer.Ordinal).ToList();

        return new PatternResult(
            cards.OrderByDescending(c => (c.ExpectedSavingMinutesPerLine ?? 0) * c.AffectedLines).ThenBy(c => c.Id, StringComparer.Ordinal).ToList(),
            insufficient.Distinct().Order(StringComparer.Ordinal).ToList(),
            unmapped);
    }
}

/// <summary>Everything the detectors share for one period: the cut-off log indexed by line and activity.</summary>
internal sealed class DetectionContext
{
    public DetectionContext(EventLog log, IReadOnlyList<LineJourney> lines, ProcessMap map, Period period, IWorkCalendar calendar)
    {
        Log = log;
        Map = map;
        Period = period;
        Calendar = calendar;
        LinesByKey = lines.ToDictionary(l => (l.Line.CaseId, l.Line.LineId));
        EventsByLine = log.Events.ToLookup(e => (e.CaseId, e.LineId));
        PeriodEvents = log.Events.Where(e => e.CompletedAt is { } c && c >= period.Start && c < period.End).ToList();
        PeriodByActivity = PeriodEvents.ToLookup(e => e.Activity);
        Links = log.Links;
        LinksByLine = (log.Links ?? []).ToLookup(l => (l.CaseId, l.LineId));
    }

    public EventLog Log { get; }
    public ProcessMap Map { get; }
    public Period Period { get; }
    public IWorkCalendar Calendar { get; }
    public IReadOnlyDictionary<(string, string), LineJourney> LinesByKey { get; }
    public ILookup<(string, string), StepEvent> EventsByLine { get; }
    public IReadOnlyList<StepEvent> PeriodEvents { get; }
    public ILookup<string, StepEvent> PeriodByActivity { get; }
    public IReadOnlyList<DocumentLink>? Links { get; }
    public ILookup<(string, string), DocumentLink> LinksByLine { get; }

    public List<StepEvent> CompletedIn(MapStep step) => PeriodByActivity[step.Name].ToList();

    public string SiteOf(StepEvent e) => LinesByKey.TryGetValue((e.CaseId, e.LineId), out var l) ? l.Line.SiteId : "";

    public double Minutes(StepEvent e, DateTimeOffset from, DateTimeOffset to) => Calendar.WorkingMinutes(from, to, SiteOf(e));

    /// <summary>Queued → completed.</summary>
    public double Total(StepEvent e) => Minutes(e, e.QueuedAt, e.CompletedAt!.Value);

    /// <summary>Queued → picked up; null without a start time.</summary>
    public double? Queue(StepEvent e) => e.StartedAt is { } s ? Minutes(e, e.QueuedAt, s) : null;
}
