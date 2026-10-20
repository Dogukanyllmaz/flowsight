using System.Globalization;
using FlowSight.Core.Journeys;
using FlowSight.Core.Model;
using FlowSight.Core.ProcessMaps;

namespace FlowSight.Core.Reasons;

/// <summary>Why an open line is waiting: a stable code plus invariant-formatted parameters. The UI turns it into a sentence.</summary>
public sealed record WaitingReason(string Code, IReadOnlyDictionary<string, string> Params);

public static class WaitingReasons
{
    public const string AssigneeAbsent = "assigneeAbsent";
    public const string ReturnedForCorrection = "returnedForCorrection";
    public const string PriceVariance = "priceVariance";
    public const string PartialDelivery = "partialDelivery";
    public const string SharedQueue = "sharedQueue";
    public const string WaitingForBatch = "waitingForBatch";
    public const string Unknown = "unknown";

    /// <summary>Invoice vs. order amount difference that counts as a variance (2 %).</summary>
    public const decimal VarianceTolerance = 0.02m;

    /// <summary>Convenience for a single line; for many lines create one <see cref="WaitingReasonEvaluator"/>.</summary>
    public static IReadOnlyList<WaitingReason> For(LineJourney line, EventLog log, ProcessMap map) =>
        new WaitingReasonEvaluator(log, map).For(line);
}

/// <summary>Indexes the log once, then evaluates the reason rules for any number of lines.</summary>
public sealed class WaitingReasonEvaluator
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly IReadOnlyDictionary<string, string> None = new Dictionary<string, string>();

    private readonly ILookup<(string, string), StepEvent> _events;
    private readonly ILookup<(string, string), DocumentLink> _links;
    private readonly IReadOnlyList<Absence> _absences;
    private readonly ProcessMap _map;
    private readonly DateOnly _today;

    public WaitingReasonEvaluator(EventLog log, ProcessMap map)
    {
        var cut = JourneyBuilder.ApplyCutOff(log);
        _events = cut.Events.ToLookup(e => (e.CaseId, e.LineId));
        _links = (cut.Links ?? []).ToLookup(l => (l.CaseId, l.LineId));
        _absences = log.Absences ?? [];
        _map = map;
        _today = DateOnly.FromDateTime(log.AsOf.DateTime);
    }

    public IReadOnlyList<WaitingReason> For(LineJourney line)
    {
        if (line.Status != LineStatus.Open || line.CurrentStage is not { } stage) return [];

        var key = (line.Line.CaseId, line.Line.LineId);
        var inStage = _events[key].Where(e => e.Stage == stage).OrderBy(e => e.QueuedAt).ToList();
        var open = inStage.LastOrDefault(e => e.CompletedAt is null);
        var links = _links[key].ToList();
        var reasons = new List<WaitingReason>();

        if (open is not null &&
            _absences.FirstOrDefault(a => a.ActorId == open.ActorId && a.DelegateId is null && _today >= a.From && _today <= a.To) is { } away)
            reasons.Add(Reason(WaitingReasons.AssigneeAbsent, ("untilDate", away.To.ToString("yyyy-MM-dd", Invariant))));

        if (inStage.LastOrDefault(e => e.CompletedAt is not null)?.Outcome == StepOutcome.Returned)
            reasons.Add(Reason(WaitingReasons.ReturnedForCorrection));

        var order = links.FirstOrDefault(l => l.Type == DocumentType.Order);
        var invoice = links.FirstOrDefault(l => l.Type == DocumentType.Invoice);
        if (stage == Stage.Invoice && order is { Amount: > 0 } && invoice is not null)
        {
            var difference = invoice.Amount / order.Amount - 1;
            if (Math.Abs(difference) > WaitingReasons.VarianceTolerance)
                reasons.Add(Reason(WaitingReasons.PriceVariance, ("percent", Math.Round(difference * 100, 1).ToString("0.0", Invariant))));
        }

        if (stage is Stage.GoodsReceipt or Stage.Invoice && order is not null)
        {
            var received = links.Where(l => l.Type == DocumentType.Receipt).Sum(l => l.Quantity);
            if (received > 0 && received < order.Quantity)
                reasons.Add(Reason(WaitingReasons.PartialDelivery,
                    ("received", received.ToString("0.##", Invariant)), ("ordered", order.Quantity.ToString("0.##", Invariant))));
        }

        if (open?.Assignment == AssignmentKind.GroupQueue)
            reasons.Add(open.CandidateCount is { } people
                ? Reason(WaitingReasons.SharedQueue, ("candidates", people.ToString(Invariant)))
                : Reason(WaitingReasons.SharedQueue));

        if (open is not null && _map.StepForActivity(open.Activity)?.WeeklyOn is { } day)
            reasons.Add(Reason(WaitingReasons.WaitingForBatch, ("day", day.ToString().ToLowerInvariant())));

        return reasons.Count > 0 ? reasons : [Reason(WaitingReasons.Unknown)];
    }

    private static WaitingReason Reason(string code, params (string Key, string Value)[] values) =>
        new(code, values.Length == 0 ? None : values.ToDictionary(v => v.Key, v => v.Value));
}
