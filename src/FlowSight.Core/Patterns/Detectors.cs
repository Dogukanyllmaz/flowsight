using FlowSight.Core.Model;
using FlowSight.Core.ProcessMaps;
using FlowSight.Core.Statistics;
using static FlowSight.Core.Patterns.PatternCodes;

namespace FlowSight.Core.Patterns;

/// <summary>
/// One detector per pattern. Each needs a <b>map structure</b> and a <b>data symptom</b>; it reports a card only when both hold,
/// and reports "insufficient data" when the structure is there but the input it needs is missing.
/// </summary>
internal static class Detectors
{
    public const double SharedQueueShare = 0.7;
    public const double SharedQueueCandidates = 3;
    public const double StackFactor = 2;
    public const double AbsenceFactor = 3;
    public const int MinimumAbsentEvents = 3;
    public const double ReturnedShare = 0.10;
    public const double WeekdayShare = 0.6;
    public const double ExceptionShare = 0.10;
    public const double ExceptionFactor = 2;
    public const decimal VarianceTolerance = 0.02m;
    public const string InvoiceMatching = "Invoice matching";

    public static void SharedQueueSearch(DetectionContext c, MapStep step, List<StepEvent> events, List<ImprovementCard> cards, List<string> insufficient)
    {
        if (step.Assignee?.Type != "group-queue") return;
        var candidates = events.Where(e => e.CandidateCount is not null).Select(e => (double)e.CandidateCount!.Value).ToList();
        if (candidates.Count == 0) { insufficient.Add($"{PatternCodes.SharedQueueSearch}:{step.Id}"); return; }

        var queue = Percentiles.Median(events.Select(c.Queue).OfType<double>().ToList());
        var total = Percentiles.Median(events.Select(c.Total).ToList());
        var people = Percentiles.Median(candidates)!.Value;
        if (queue is null || total is not > 0) return;

        var share = queue.Value / total.Value;
        if (share < SharedQueueShare || people < SharedQueueCandidates) return;

        cards.Add(Card(PatternCodes.SharedQueueSearch, step, events, null, $"assignee: {step.Assignee.Type}", RouteByAttribute, queue, Lines(events),
            Ev("medianQueue", queue.Value), Ev("medianStep", total.Value), Ev("queueShare", share), Ev("candidates", people)));
    }

    public static void SerialApprovalStack(DetectionContext c, IReadOnlyList<MapStep> chain, List<ImprovementCard> cards)
    {
        var names = chain.Select(s => s.Name).ToList();
        var lineTotals = new List<double>();
        var singles = new List<double>();
        var lastEvents = new List<StepEvent>();

        foreach (var last in c.CompletedIn(chain[^1]))
        {
            var steps = names.Select(n => c.EventsByLine[(last.CaseId, last.LineId)]
                .LastOrDefault(e => e.Activity == n && e.CompletedAt is not null)).ToList();
            if (steps.Any(s => s is null)) continue;
            var durations = steps.Select(s => c.Total(s!)).ToList();
            lineTotals.Add(durations.Sum());
            singles.AddRange(durations);
            lastEvents.Add(last);
        }
        if (lastEvents.Count < PatternLibrary.MinimumEvents) return;

        var medianTotal = Percentiles.Median(lineTotals)!.Value;
        var medianSingle = Percentiles.Median(singles)!.Value;
        if (medianTotal < StackFactor * medianSingle) return;

        cards.Add(Card(PatternCodes.SerialApprovalStack, chain[0], lastEvents, null,
            $"approval chain: {string.Join(" → ", chain.Select(s => s.Id))}", ParallelOrThresholdApproval,
            medianTotal - medianSingle, Lines(lastEvents),
            Ev("approvalsInChain", chain.Count), Ev("medianChainTotal", medianTotal), Ev("medianSingleApproval", medianSingle)));
    }

    public static void SinglePointOfFailure(DetectionContext c, MapStep step, List<StepEvent> events, List<ImprovementCard> cards, List<string> insufficient)
    {
        if (step.Assignee?.Type != "person") return;
        var absences = c.Log.Absences;
        if (absences is null || absences.Count == 0) { insufficient.Add($"{PatternCodes.SinglePointOfFailure}:{step.Id}"); return; }

        var uncovered = absences.Where(a => a.DelegateId is null).ToList();
        bool During(StepEvent e)
        {
            var day = DateOnly.FromDateTime(e.QueuedAt.ToOffset(c.Period.End.Offset).DateTime);
            return uncovered.Any(a => a.ActorId == e.ActorId && day >= a.From && day <= a.To);
        }

        var absent = events.Where(During).ToList();
        var present = events.Where(e => !During(e)).ToList();
        if (absent.Count < MinimumAbsentEvents) return;

        var queueAbsent = Percentiles.Median(absent.Select(c.Queue).OfType<double>().ToList());
        var queuePresent = Percentiles.Median(present.Select(c.Queue).OfType<double>().ToList());
        if (queueAbsent is null || queuePresent is not > 0 || queueAbsent < AbsenceFactor * queuePresent) return;

        // Absence days without a stand-in that fall inside the period.
        var first = DateOnly.FromDateTime(c.Period.Start.DateTime);
        var last = DateOnly.FromDateTime(c.Period.End.AddTicks(-1).DateTime);
        var days = uncovered
            .Where(a => absent.Any(e => e.ActorId == a.ActorId))
            .Sum(a => Math.Max(0, Math.Min(a.To.DayNumber, last.DayNumber) - Math.Max(a.From.DayNumber, first.DayNumber) + 1));

        cards.Add(Card(PatternCodes.SinglePointOfFailure, step, absent, null, $"assignee: {step.Assignee.Type}", AssignToRoleWithDelegation,
            queueAbsent - queuePresent, Lines(absent),
            Ev("medianQueueAbsent", queueAbsent.Value), Ev("medianQueuePresent", queuePresent.Value), Ev("absenceDays", days)));
    }

    public static void ReworkLoop(DetectionContext c, MapStep step, List<StepEvent> events, List<ImprovementCard> cards)
    {
        if (step.ReturnTo is null) return;

        // Rework is often concentrated in one kind of request: check the whole step, then each item category.
        var byLine = events.GroupBy(e => (e.CaseId, e.LineId)).ToList();
        var segments = new List<(string? Segment, List<IGrouping<(string, string), StepEvent>> Lines)> { (null, byLine) };
        segments.AddRange(byLine
            .GroupBy(g => c.LinesByKey.TryGetValue(g.Key, out var l) ? l.Line.ItemCategory : "")
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => ((string?)g.Key, g.ToList())));

        var best = segments
            .Where(s => s.Lines.Count >= PatternLibrary.MinimumEvents)
            .Select(s =>
            {
                var returned = s.Lines.Where(g => c.EventsByLine[g.Key].Any(e => e.Activity == step.Name && e.Outcome == StepOutcome.Returned)).ToList();
                return (s.Segment, s.Lines, Returned: returned, Share: returned.Count / (double)s.Lines.Count);
            })
            .Where(s => s.Share >= ReturnedShare)
            .OrderByDescending(s => s.Share)
            .FirstOrDefault();
        if (best.Lines is null) return;

        // Time from the first time the line reached the step to its final completion, with and without a return.
        double Span((string, string) key)
        {
            var mine = c.EventsByLine[key].Where(e => e.CompletedAt is not null).OrderBy(e => e.QueuedAt).ToList();
            var first = mine.First(e => e.Activity == step.Name);
            var lastDone = mine.Last(e => e.Activity == step.Name);
            return c.Minutes(first, first.QueuedAt, lastDone.CompletedAt!.Value);
        }
        var returnedKeys = best.Returned.Select(g => g.Key).ToHashSet();
        var with = Percentiles.Median(best.Returned.Select(g => Span(g.Key)).ToList())!.Value;
        var without = Percentiles.Median(best.Lines.Where(g => !returnedKeys.Contains(g.Key)).Select(g => Span(g.Key)).ToList());

        // Evidence that cannot be computed is left out rather than shown as zero.
        Evidence[] evidence = without is null
            ? [Ev("returnedShare", best.Share), Ev("medianWithRework", with)]
            : [Ev("returnedShare", best.Share), Ev("medianWithRework", with), Ev("medianWithoutRework", without.Value)];
        cards.Add(Card(PatternCodes.ReworkLoop, step, best.Returned.SelectMany(g => g).ToList(), best.Segment, $"return_to: {step.ReturnTo}", ValidateAtEntry,
            without is null ? null : with - without.Value, best.Returned.Count, evidence));
    }

    public static void BatchWaiting(DetectionContext c, MapStep step, List<StepEvent> events, List<ImprovementCard> cards)
    {
        if (step.WeeklyOn is null || step.SlaHours is not null) return;

        var top = events
            .GroupBy(e => e.CompletedAt!.Value.ToOffset(c.Period.End.Offset).DayOfWeek)
            .OrderByDescending(g => g.Count())
            .First();
        var share = top.Count() / (double)events.Count;
        if (share < WeekdayShare) return;

        // After pick-up the work is short; what remains is waiting for the weekly run.
        var afterPickUp = Percentiles.Median(events.Where(e => e.StartedAt is not null)
            .Select(e => c.Minutes(e, e.StartedAt!.Value, e.CompletedAt!.Value)).ToList());

        var weekday = new Evidence("weekdayShare", new Dictionary<string, double> { ["value"] = Math.Round(share, 3), ["weekday"] = (int)top.Key });
        cards.Add(Card(PatternCodes.BatchWaiting, step, events, null, $"schedule: weekly:{step.WeeklyOn.Value.ToString().ToLowerInvariant()}, no SLA",
            DailyProcessingWithSla, afterPickUp, Lines(events),
            afterPickUp is null ? [weekday] : [weekday, Ev("medianAfterPickUp", afterPickUp.Value)]));
    }

    public static void ExceptionQueue(DetectionContext c, MapStep step, List<StepEvent> events, List<ImprovementCard> cards, List<string> insufficient)
    {
        if (step.Name != InvoiceMatching) return;
        if (c.Links is null || c.Links.Count == 0) { insufficient.Add($"{PatternCodes.ExceptionQueue}:{step.Id}"); return; }

        bool? Varies(StepEvent e)
        {
            var links = c.LinksByLine[(e.CaseId, e.LineId)].ToList();
            var order = links.FirstOrDefault(l => l.Type == DocumentType.Order);
            var invoice = links.FirstOrDefault(l => l.Type == DocumentType.Invoice);
            if (order is not { Amount: > 0 } || invoice is null) return null;
            return Math.Abs(invoice.Amount / order.Amount - 1) > VarianceTolerance;
        }

        var judged = events.Select(e => (Event: e, Varies: Varies(e))).Where(x => x.Varies is not null).ToList();
        var exceptions = judged.Where(x => x.Varies == true).Select(x => x.Event).ToList();
        var normal = judged.Where(x => x.Varies == false).Select(x => x.Event).ToList();
        if (judged.Count < PatternLibrary.MinimumEvents || exceptions.Count == 0 || normal.Count == 0) return;

        var share = exceptions.Count / (double)judged.Count;
        var medianException = Percentiles.Median(exceptions.Select(c.Total).ToList())!.Value;
        var medianNormal = Percentiles.Median(normal.Select(c.Total).ToList())!.Value;
        if (share < ExceptionShare || medianException < ExceptionFactor * medianNormal) return;

        cards.Add(Card(PatternCodes.ExceptionQueue, step, exceptions, null, "matching step", ToleranceRulesUpstream,
            medianException - medianNormal, Lines(exceptions),
            Ev("exceptionShare", share), Ev("medianException", medianException), Ev("medianNormal", medianNormal)));
    }

    private static ImprovementCard Card(string pattern, MapStep step, List<StepEvent> events, string? segment, string mapProperty,
        string change, double? saving, int affected, params Evidence[] evidence) =>
        new($"{pattern}:{step.Id}", pattern, step.Stage ?? events[0].Stage, step.Id, step.Name, segment, mapProperty,
            evidence, change, saving is null ? null : Math.Round(saving.Value), affected);

    private static Evidence Ev(string code, double value) => new(code, new Dictionary<string, double> { ["value"] = Math.Round(value, 3) });

    private static int Lines(IEnumerable<StepEvent> events) => events.Select(e => (e.CaseId, e.LineId)).Distinct().Count();
}
