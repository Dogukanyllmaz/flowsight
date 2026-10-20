using FlowSight.Core.Calendar;
using FlowSight.Core.Model;
using FlowSight.Synthetic;

namespace FlowSight.Tests;

/// <summary>Ground truth for the five M3 scenarios and the document / absence inputs.</summary>
public class ScenarioM3Tests
{
    private static readonly WorkCalendar Calendar = new(CalendarRules.Default);
    private static readonly Lazy<EventLog> Log = new(() => Generator.Generate(new SyntheticOptions(), Calendar));

    private static readonly DateOnly[] EastWindowStarts = [new(2026, 2, 2), new(2026, 8, 3)];

    private static Dictionary<(string, string), RequestLine> Lines() => Log.Value.Lines.ToDictionary(l => (l.CaseId, l.LineId));

    [Fact]
    public void ItRequests_HaveThreeSerialApprovals()
    {
        var itCases = Log.Value.Lines.Where(l => l.ItemCategory == "IT hardware").Select(l => l.CaseId).ToHashSet();
        var chains = Log.Value.Events
            .Where(e => itCases.Contains(e.CaseId) && e.Stage == Stage.Approval && e.LineId == "L1" && e.CompletedAt is not null)
            .GroupBy(e => e.CaseId)
            .Where(g => g.All(e => e.Outcome == StepOutcome.Approved) && g.Count() == 3)
            .ToList();

        Assert.NotEmpty(chains);
        Assert.All(chains, g => Assert.Equal(["IT approval", "Line manager approval", "Budget owner approval"],
            g.OrderBy(e => e.QueuedAt).Select(e => e.Activity)));
    }

    [Fact]
    public void SiteEastTreasurer_AbsentWithoutDelegate()
    {
        var absences = Log.Value.Absences!;
        var east = Catalog.Actors[("Site East", Catalog.Treasury)][0];
        var north = Catalog.Actors[("Site North", Catalog.Treasury)][0];

        Assert.Equal(EastWindowStarts, absences.Where(a => a.ActorId == east).Select(a => a.From));
        Assert.All(absences.Where(a => a.ActorId == east), a => Assert.Null(a.DelegateId));
        Assert.Contains(absences, a => a.ActorId == north && a.DelegateId is not null);
    }

    [Fact]
    public void PaymentsQueuedDuringAbsence_WaitForReturn()
    {
        var lines = Lines();
        var windows = Log.Value.Absences!.Where(a => a.DelegateId is null).ToList();
        var east = Log.Value.Events.Where(e => e.Stage == Stage.Payment && lines[(e.CaseId, e.LineId)].SiteId == "Site East").ToList();

        var inWindow = east.Where(e => windows.Any(w => In(e.QueuedAt, w))).ToList();
        Assert.NotEmpty(inWindow);
        Assert.All(inWindow.Where(e => e.StartedAt is not null), e =>
        {
            var w = windows.First(x => In(e.QueuedAt, x));
            Assert.True(DateOnly.FromDateTime(e.StartedAt!.Value.ToOffset(CalendarRules.Default.UtcOffset).DateTime) > w.To);
        });
    }

    [Fact]
    public void MaintenanceService_IsReturnedAboutOneInFive()
    {
        var lines = Lines();
        var maintenance = Log.Value.Events.Where(e => lines[(e.CaseId, e.LineId)].ItemCategory == "Maintenance service")
            .GroupBy(e => (e.CaseId, e.LineId))
            .Where(g => g.Any(e => e.Activity == "Sourcing and quotes" && e.CompletedAt is not null))
            .ToList();
        var returned = maintenance.Where(g => g.Any(e => e.Outcome == StepOutcome.Returned)).ToList();

        Assert.InRange(returned.Count / (double)maintenance.Count, 0.15, 0.25);
        Assert.All(returned.Where(g => g.Count(e => e.Activity == "Sourcing and quotes") == 2), g =>
            Assert.Equal(["Sourcing and quotes", "Clarify specification", "Sourcing and quotes"],
                g.Where(e => e.Stage == Stage.Purchasing).OrderBy(e => e.QueuedAt).Select(e => e.Activity)));
    }

    [Fact]
    public void Payments_CompleteOnFridays()
    {
        var done = Log.Value.Events.Where(e => e.Stage == Stage.Payment && e.CompletedAt is not null).ToList();
        Assert.NotEmpty(done);
        Assert.All(done, e => Assert.Equal(DayOfWeek.Friday, e.CompletedAt!.Value.ToOffset(CalendarRules.Default.UtcOffset).DayOfWeek));
    }

    [Fact]
    public void PriceVariance_OnAboutOneInTenInvoices()
    {
        var links = Log.Value.Links!;
        var orders = links.Where(l => l.Type == DocumentType.Order).ToDictionary(l => (l.CaseId, l.LineId), l => l.Amount);
        var invoices = links.Where(l => l.Type == DocumentType.Invoice).ToList();
        var varied = invoices.Count(i => Math.Abs(i.Amount / orders[(i.CaseId, i.LineId)] - 1) > 0.02m);

        Assert.InRange(varied / (double)invoices.Count, 0.07, 0.13);
    }

    [Fact]
    public void PartialDeliveries_HaveTwoReceipts()
    {
        var links = Log.Value.Links!;
        var ordered = links.Where(l => l.Type == DocumentType.Order).ToDictionary(l => (l.CaseId, l.LineId), l => l.Quantity);
        var receipts = links.Where(l => l.Type == DocumentType.Receipt).GroupBy(l => (l.CaseId, l.LineId)).ToList();
        var goodsWithReceipts = receipts.Count(g => Lines()[g.Key].Path == LinePath.Goods);
        var partial = receipts.Where(g => g.Count() == 2).ToList();

        Assert.InRange(partial.Count / (double)goodsWithReceipts, 0.05, 0.11);
        Assert.All(partial, g => Assert.Equal(ordered[g.Key], g.Sum(r => r.Quantity)));
    }

    [Fact]
    public void LinksExist_ForEveryOrderedLine()
    {
        var lines = Lines();
        var orders = Log.Value.Links!.Where(l => l.Type == DocumentType.Order).ToDictionary(l => (l.CaseId, l.LineId));
        var ordered = Log.Value.Events.Where(e => e.Stage == Stage.Order && e.CompletedAt is not null).ToList();

        Assert.NotEmpty(ordered);
        Assert.All(ordered, e =>
        {
            var link = orders[(e.CaseId, e.LineId)];
            Assert.Equal(lines[(e.CaseId, e.LineId)].Quantity, link.Quantity);
            Assert.Equal(lines[(e.CaseId, e.LineId)].Amount, link.Amount);
        });
        Assert.All(Log.Value.Links!, l => Assert.True(l.At <= Log.Value.AsOf));
    }

    private static bool In(DateTimeOffset t, Absence a)
    {
        var day = DateOnly.FromDateTime(t.ToOffset(CalendarRules.Default.UtcOffset).DateTime);
        return day >= a.From && day <= a.To;
    }
}
