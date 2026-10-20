using FlowSight.Core;
using FlowSight.Core.Calendar;
using FlowSight.Core.Model;
using static FlowSight.Synthetic.Catalog;

namespace FlowSight.Synthetic;

/// <summary>Generates a deterministic purchase-to-pay event log. Same options and calendar, same log.</summary>
public static class Generator
{
    public static EventLog Generate(SyntheticOptions options, IWorkCalendar calendar)
    {
        var random = new Random(options.Seed);
        var offset = CalendarRules.Default.UtcOffset;
        var periodStart = new DateTimeOffset(options.StartDate.ToDateTime(new TimeOnly(8, 0)), offset);
        var periodEnd = new DateTimeOffset(options.StartDate.AddMonths(options.Months).ToDateTime(new TimeOnly(8, 0)), offset);
        var asOf = options.AsOf ?? calendar.AddWorkingMinutes(periodEnd, 0);
        var change = new DateTimeOffset(options.ChangeDate.ToDateTime(TimeOnly.MinValue), offset);

        var periodMinutes = calendar.WorkingMinutes(periodStart, periodEnd);
        var arrivals = Enumerable.Range(0, options.Requests)
            .Select(_ => Math.Floor(random.NextDouble() * periodMinutes))
            .Order()
            .Select(m => calendar.AddWorkingMinutes(periodStart, m))
            .ToList();

        var lines = new List<RequestLine>();
        var events = new List<StepEvent>();
        for (var i = 0; i < arrivals.Count; i++)
        {
            var request = new RequestBuilder(random, calendar, asOf, change, $"PR-{i + 1:000000}", Sites[random.Next(Sites.Length)]);
            request.Build(arrivals[i], lines, events);
        }

        return new EventLog(lines, events, asOf);
    }

    private sealed class RequestBuilder(Random random, IWorkCalendar calendar, DateTimeOffset asOf, DateTimeOffset change, string caseId, string site)
    {
        public void Build(DateTimeOffset arrival, List<RequestLine> lines, List<StepEvent> events)
        {
            var requestLines = Enumerable.Range(1, LineCount()).Select(n => NewLine($"L{n}")).ToList();
            lines.AddRange(requestLines);

            // Request and approval happen once per request and apply to every line.
            var requester = Pick(Requester);
            var shared = new List<(StepTemplate Step, string Actor, DateTimeOffset Queued, double Minutes, StepOutcome Outcome)>();
            var time = arrival;
            var rejected = false;

            foreach (var step in ApprovalChain(requestLines))
            {
                var minutes = Duration(step);
                var isLast = step == BudgetApproval || (step == ManagerApproval && requestLines.Sum(l => l.Amount) <= BudgetApprovalThreshold);
                var outcome = step.Stage == Stage.Request ? StepOutcome.Done
                    : isLast && random.NextDouble() < RejectionRate ? StepOutcome.Rejected
                    : StepOutcome.Approved;
                shared.Add((step, step == CreateRequest ? requester : Pick(step.Role), time, minutes, outcome));
                time = calendar.AddWorkingMinutes(time, minutes, site);
                if (outcome == StepOutcome.Rejected) { rejected = true; break; }
            }

            foreach (var line in requestLines)
            {
                var stillRunning = shared.All(s => Emit(events, line, s.Step, s.Actor, s.Queued, s.Minutes, s.Outcome));
                if (rejected || !stillRunning) continue;

                var t = time;
                foreach (var planned in LineChain(line))
                {
                    var (step, actor, candidates) = planned.Stage == Stage.Invoice
                        ? InvoiceStep(t)
                        : (planned, planned.Role == Requester ? requester : Pick(planned.Role), (int?)null);
                    var minutes = Duration(step);
                    var outcome = step.Stage == Stage.Purchasing && random.NextDouble() < CancellationRate
                        ? StepOutcome.Cancelled
                        : StepOutcome.Done;
                    if (!Emit(events, line, step, actor, t, minutes, outcome, candidates) || outcome == StepOutcome.Cancelled) break;
                    t = calendar.AddWorkingMinutes(t, minutes, site);
                }
            }
        }

        /// <summary>Adds one event, cut off at the as-of date. Returns false when the chain must stop here.</summary>
        /// <summary>Invoice handling before and after the injected routing change.</summary>
        private (StepTemplate Step, string Actor, int? Candidates) InvoiceStep(DateTimeOffset queued) =>
            queued < change
                ? (SharedInvoiceQueue, AllClerks[random.Next(AllClerks.Length)], AllClerks.Length)
                : (RoutedInvoice, RoutedClerk(site), 1);

        private bool Emit(List<StepEvent> events, RequestLine line, StepTemplate step, string actor, DateTimeOffset queued, double minutes, StepOutcome outcome, int? candidates = null)
        {
            if (queued > asOf) return false;
            var started = calendar.AddWorkingMinutes(queued, Math.Round(minutes * step.QueueShare), site);
            var completed = calendar.AddWorkingMinutes(queued, minutes, site);
            var done = completed <= asOf;

            events.Add(new StepEvent(
                caseId, line.LineId, step.Stage, step.Activity, actor, step.Role, step.Assignment,
                queued,
                started <= asOf ? started : null,
                done ? completed : null,
                done ? outcome : null,
                candidates));
            return done;
        }

        private IEnumerable<StepTemplate> ApprovalChain(List<RequestLine> requestLines)
        {
            yield return CreateRequest;
            yield return ManagerApproval;
            if (requestLines.Sum(l => l.Amount) > BudgetApprovalThreshold) yield return BudgetApproval;
        }

        private static IEnumerable<StepTemplate> LineChain(RequestLine line)
        {
            yield return Sourcing;
            yield return CreateOrder;
            yield return line.Path == LinePath.Goods ? GoodsReceipt : ServiceAcceptance;
            yield return SharedInvoiceQueue; // replaced by InvoiceStep at generation time
            yield return PaymentRun;
        }

        private RequestLine NewLine(string lineId)
        {
            var goods = random.NextDouble() < GoodsShare;
            var options = Categories.Where(c => c.Path == (goods ? LinePath.Goods : LinePath.Service)).ToArray();
            var (category, path) = options[random.Next(options.Length)];
            var quantity = random.Next(1, 21);
            var amount = Math.Round((decimal)LogNormal(900, 1.0), 2);
            return new RequestLine(caseId, lineId, site, category, path, quantity, amount, "EUR");
        }

        private int LineCount()
        {
            var r = random.NextDouble();
            return r < 0.5 ? 1 : r < 0.8 ? 2 : r < 0.95 ? 3 : 4;
        }

        private string Pick(string role)
        {
            var pool = Actors[(site, role)];
            return pool[random.Next(pool.Length)];
        }

        private double Duration(StepTemplate step) => Math.Round(LogNormal(step.MedianMinutes, DurationSigma));

        private double LogNormal(double median, double sigma)
        {
            // Box–Muller transform on the seeded generator.
            var u1 = 1.0 - random.NextDouble();
            var u2 = random.NextDouble();
            var normal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            return median * Math.Exp(sigma * normal);
        }
    }
}

/// <summary>Event source backed by the synthetic generator.</summary>
public sealed class SyntheticEventSource(SyntheticOptions options, IWorkCalendar calendar) : IEventSource
{
    public EventLog Load() => Generator.Generate(options, calendar);
}
