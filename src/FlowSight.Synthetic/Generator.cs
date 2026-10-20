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

        var output = new Output();
        for (var i = 0; i < arrivals.Count; i++)
        {
            var request = new RequestBuilder(random, calendar, asOf, change, $"PR-{i + 1:000000}", Sites[random.Next(Sites.Length)], output);
            request.Build(arrivals[i]);
        }

        return new EventLog(output.Lines, output.Events, asOf, output.Links, Absences);
    }

    private sealed class Output
    {
        public List<RequestLine> Lines { get; } = new();
        public List<StepEvent> Events { get; } = new();
        public List<DocumentLink> Links { get; } = new();
    }

    private sealed class RequestBuilder(
        Random random, IWorkCalendar calendar, DateTimeOffset asOf, DateTimeOffset change, string caseId, string site, Output output)
    {
        public void Build(DateTimeOffset arrival)
        {
            var requestLines = Enumerable.Range(1, LineCount()).Select(n => NewLine($"L{n}")).ToList();
            output.Lines.AddRange(requestLines);

            // Request and approval happen once per request and apply to every line.
            var requester = Pick(Requester);
            var chain = ApprovalChain(requestLines).ToList();
            var shared = new List<(StepTemplate Step, string Actor, DateTimeOffset Queued, DateTimeOffset Started, DateTimeOffset Completed, StepOutcome Outcome)>();
            var time = arrival;
            var rejected = false;

            foreach (var step in chain)
            {
                var minutes = Duration(step);
                var isLast = step == chain[^1];
                var outcome = step.Stage == Stage.Request ? StepOutcome.Done
                    : isLast && random.NextDouble() < RejectionRate ? StepOutcome.Rejected
                    : StepOutcome.Approved;
                var (started, completed) = Schedule(time, minutes, step.QueueShare);
                shared.Add((step, step == CreateRequest ? requester : Pick(step.Role), time, started, completed, outcome));
                time = completed;
                if (outcome == StepOutcome.Rejected) { rejected = true; break; }
            }

            foreach (var line in requestLines)
            {
                var stillRunning = shared.All(s => Emit(line, s.Step, s.Actor, s.Queued, s.Started, s.Completed, s.Outcome));
                if (rejected || !stillRunning) continue;
                RunLine(line, requester, time);
            }
        }

        private void RunLine(RequestLine line, string requester, DateTimeOffset t)
        {
            // Purchasing: maintenance services are sometimes sent back for a clearer specification.
            if (line.ItemCategory == ReworkCategory && random.NextDouble() < ReturnRate)
            {
                if (!Step(line, Sourcing, Pick(Buyer), ref t, StepOutcome.Returned)) return;
                if (!Step(line, ClarifySpecification, requester, ref t, StepOutcome.Done)) return;
            }
            var cancelled = random.NextDouble() < CancellationRate;
            if (!Step(line, Sourcing, Pick(Buyer), ref t, cancelled ? StepOutcome.Cancelled : StepOutcome.Done) || cancelled) return;

            // Order
            if (!Step(line, CreateOrder, Pick(Buyer), ref t, StepOutcome.Done)) return;
            AddLink(line, DocumentType.Order, $"PO-{caseId}-{line.LineId}", line.Quantity, line.Amount, t);

            // Receipt: some goods arrive in two deliveries.
            if (!Receive(line, requester, ref t)) return;

            // Invoice: some invoices differ from the order and need exception handling.
            var variance = random.NextDouble() < PriceVarianceRate;
            var invoiceAmount = variance
                ? Math.Round(line.Amount * (decimal)(1 + (random.NextDouble() < 0.5 ? -1 : 1) * (0.03 + 0.07 * random.NextDouble())), 2)
                : line.Amount;
            var extra = variance ? Math.Round(LogNormal(ExceptionMedianMinutes, DurationSigma)) : 0;
            var (invoiceStep, clerk, candidates) = InvoiceStep(t);
            AddLink(line, DocumentType.Invoice, $"INV-{caseId}-{line.LineId}", line.Quantity, invoiceAmount, t);
            var (started, completed) = Schedule(t, Duration(invoiceStep) + extra, invoiceStep.QueueShare);
            if (!Emit(line, invoiceStep, clerk, t, started, completed, StepOutcome.Done, candidates)) return;
            t = completed;

            Pay(line, t);
        }

        private bool Receive(RequestLine line, string requester, ref DateTimeOffset t)
        {
            if (line.Path == LinePath.Service)
            {
                if (!Step(line, ServiceAcceptance, requester, ref t, StepOutcome.Done)) return false;
                AddLink(line, DocumentType.Receipt, $"GR-{caseId}-{line.LineId}-1", line.Quantity, line.Amount, t);
                return true;
            }

            var partial = line.Quantity >= 2 && random.NextDouble() < PartialDeliveryRate;
            var baseMinutes = Duration(GoodsReceipt);
            var backorder = partial ? Math.Round(LogNormal(BackorderMedianMinutes, DurationSigma)) : 0;
            var (started, completed) = Schedule(t, baseMinutes + backorder, GoodsReceipt.QueueShare);
            var clerk = Pick(WarehouseClerk);
            if (partial)
            {
                var first = Math.Max(1, Math.Floor(line.Quantity * (decimal)(0.5 + 0.4 * random.NextDouble())));
                var firstAt = calendar.AddWorkingMinutes(t, baseMinutes, site);
                AddLink(line, DocumentType.Receipt, $"GR-{caseId}-{line.LineId}-1", first, Share(line, first), firstAt);
                AddLink(line, DocumentType.Receipt, $"GR-{caseId}-{line.LineId}-2", line.Quantity - first, line.Amount - Share(line, first), completed);
            }
            else
            {
                AddLink(line, DocumentType.Receipt, $"GR-{caseId}-{line.LineId}-1", line.Quantity, line.Amount, completed);
            }
            if (!Emit(line, GoodsReceipt, clerk, t, started, completed, StepOutcome.Done)) return false;
            t = completed;
            return true;
        }

        /// <summary>Payment by the site's treasurer, in the weekly Friday run; it waits while the treasurer is away with no stand-in.</summary>
        private void Pay(RequestLine line, DateTimeOffset queued)
        {
            var actor = Treasurer(site);
            DateTimeOffset? notBefore = null;
            var day = DateOnly.FromDateTime(queued.ToOffset(CalendarRules.Default.UtcOffset).DateTime);
            var absence = Absences.FirstOrDefault(a => a.ActorId == actor && day >= a.From && day <= a.To);
            if (absence is not null)
            {
                if (absence.DelegateId is not null) actor = absence.DelegateId;
                else notBefore = new DateTimeOffset(absence.To.AddDays(1).ToDateTime(TimeOnly.MinValue), CalendarRules.Default.UtcOffset);
            }

            var (started, natural) = Schedule(queued, Duration(PaymentRun), PaymentRun.QueueShare, notBefore);
            Emit(line, PaymentRun, actor, queued, started, NextPaymentRun(natural), StepOutcome.Done);
        }

        private DateTimeOffset NextPaymentRun(DateTimeOffset ready)
        {
            var local = ready.ToOffset(CalendarRules.Default.UtcOffset);
            for (var date = DateOnly.FromDateTime(local.DateTime); ; date = date.AddDays(1))
            {
                if (date.DayOfWeek != DayOfWeek.Friday) continue;
                var run = new DateTimeOffset(date.ToDateTime(PaymentRunTime), CalendarRules.Default.UtcOffset);
                if (run >= ready && calendar.WorkingMinutes(run, run.AddMinutes(1), site) == 1) return run;
            }
        }

        /// <summary>Schedules and emits a plain step; advances <paramref name="t"/> to its completion.</summary>
        private bool Step(RequestLine line, StepTemplate step, string actor, ref DateTimeOffset t, StepOutcome outcome)
        {
            var (started, completed) = Schedule(t, Duration(step), step.QueueShare);
            if (!Emit(line, step, actor, t, started, completed, outcome)) return false;
            t = completed;
            return true;
        }

        private (DateTimeOffset Started, DateTimeOffset Completed) Schedule(DateTimeOffset queued, double minutes, double queueShare, DateTimeOffset? notBefore = null)
        {
            var queueMinutes = Math.Round(minutes * queueShare);
            var started = calendar.AddWorkingMinutes(queued, queueMinutes, site);
            if (notBefore is { } nb && nb > started) started = calendar.AddWorkingMinutes(nb, 0, site);
            return (started, calendar.AddWorkingMinutes(started, minutes - queueMinutes, site));
        }

        /// <summary>Adds one event, cut off at the as-of date. Returns false when the chain must stop here.</summary>
        private bool Emit(RequestLine line, StepTemplate step, string actor, DateTimeOffset queued, DateTimeOffset started, DateTimeOffset completed, StepOutcome outcome, int? candidates = null)
        {
            if (queued > asOf) return false;
            var done = completed <= asOf;
            output.Events.Add(new StepEvent(
                caseId, line.LineId, step.Stage, step.Activity, actor, step.Role, step.Assignment,
                queued,
                started <= asOf ? started : null,
                done ? completed : null,
                done ? outcome : null,
                candidates));
            return done;
        }

        private void AddLink(RequestLine line, DocumentType type, string id, decimal quantity, decimal amount, DateTimeOffset at)
        {
            if (at <= asOf) output.Links.Add(new DocumentLink(caseId, line.LineId, type, id, quantity, amount, at));
        }

        private static decimal Share(RequestLine line, decimal quantity) => Math.Round(line.Amount * quantity / line.Quantity, 2);

        /// <summary>Invoice handling before and after the injected routing change.</summary>
        private (StepTemplate Step, string Actor, int? Candidates) InvoiceStep(DateTimeOffset queued) =>
            queued < change
                ? (SharedInvoiceQueue, AllClerks[random.Next(AllClerks.Length)], AllClerks.Length)
                : (RoutedInvoice, RoutedClerk(site), 1);

        private static IEnumerable<StepTemplate> ApprovalChain(List<RequestLine> requestLines)
        {
            yield return CreateRequest;
            if (requestLines.Any(l => l.ItemCategory == ItCategory))
            {
                yield return ItApproval;
                yield return ManagerApproval;
                yield return BudgetApproval;
                yield break;
            }
            yield return ManagerApproval;
            if (requestLines.Sum(l => l.Amount) > BudgetApprovalThreshold) yield return BudgetApproval;
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
