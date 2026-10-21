using FlowSight.Core.Calendar;
using FlowSight.Core.Impact;
using FlowSight.Core.Model;
using FlowSight.Core.ProcessMaps;
using FlowSight.Synthetic;
using static FlowSight.Tests.TestCalendar;

namespace FlowSight.Tests;

public class ChangeImpactTests
{
    private static readonly WorkCalendar Calendar = new(CalendarRules.Default);
    private static readonly Lazy<EventLog> Log = new(() => Generator.Generate(new SyntheticOptions(), Calendar));

    /// <summary>The sample map with extra changes appended to its `changes:` list.</summary>
    private static ProcessMap MapWith(params (string Date, string Step)[] extra) =>
        ProcessMapYaml.Parse(ProcessMapTests.SampleYaml() + string.Concat(extra.Select(c =>
            $"\n  - date: {c.Date}\n    step: {c.Step}\n    description: Test change\n")));

    private static IReadOnlyList<ChangeImpact> Measure(ProcessMap? map = null, EventLog? log = null) =>
        ChangeImpacts.Measure(log ?? Log.Value, map ?? ProcessMapTests.Sample(), Calendar);

    [Fact]
    public void InvoiceRouting_IsMeasuredAsImproved()
    {
        var impact = Assert.Single(Measure());

        Assert.Equal("invoice-matching", impact.StepId);
        Assert.Equal(Stage.Invoice, impact.Stage);
        Assert.Equal(T("2026-03-05T00:00+03:00"), impact.Before.Start);
        Assert.Equal(T("2026-06-03T00:00+03:00"), impact.Before.End);
        Assert.Equal(T("2026-09-01T00:00+03:00"), impact.After.End);
        Assert.Equal(ImpactVerdict.Improved, impact.Verdict);
        Assert.True(impact.MedianChangePercent <= -70, $"change was {impact.MedianChangePercent}");
        Assert.True(impact.AfterStats.AverageWaiting < impact.BeforeStats.AverageWaiting);
        Assert.True(impact.BeforeStats.Steps >= 20 && impact.AfterStats.Steps >= 20);
    }

    [Fact]
    public void MonthlySeries_CoversBothWindows()
    {
        var impact = Assert.Single(Measure());

        Assert.Equal(["2026-03", "2026-04", "2026-05", "2026-06", "2026-07", "2026-08"], impact.Monthly.Select(m => m.PeriodId));
        Assert.All(impact.Monthly, m => Assert.True(m.Steps > 0));
    }

    [Fact]
    public void UnrelatedStep_IsUnchanged()
    {
        var order = Measure(MapWith(("2026-06-03", "create-order"))).Single(i => i.StepId == "create-order");

        Assert.Equal(ImpactVerdict.Unchanged, order.Verdict);
    }

    [Fact]
    public void ChangeOutsideTheData_IsInsufficient()
    {
        var impacts = Measure(MapWith(("2025-01-01", "create-order"), ("2027-01-01", "goods-receipt")));

        Assert.Equal(ImpactVerdict.InsufficientData, impacts.Single(i => i.StepId == "create-order").Verdict);
        Assert.Equal(ImpactVerdict.InsufficientData, impacts.Single(i => i.StepId == "goods-receipt").Verdict);
        // No working day was sampled, so there is no average — not an average of zero.
        Assert.Null(impacts.Single(i => i.StepId == "goods-receipt").AfterStats.AverageWaiting);
    }

    [Fact]
    public void ChangeOnUnusedStep_IsInsufficient()
    {
        var log = Log.Value;
        var kept = log.Lines.Where(l => l.ItemCategory != "Maintenance service").ToList();
        var keys = kept.Select(l => (l.CaseId, l.LineId)).ToHashSet();
        var withoutMaintenance = log with { Lines = kept, Events = log.Events.Where(e => keys.Contains((e.CaseId, e.LineId))).ToList() };

        var clarify = Measure(MapWith(("2026-06-03", "clarify-spec")), withoutMaintenance).Single(i => i.StepId == "clarify-spec");

        Assert.Equal(ImpactVerdict.InsufficientData, clarify.Verdict);
        Assert.Equal(0, clarify.BeforeStats.Steps + clarify.AfterStats.Steps);
        Assert.Null(clarify.MedianChangePercent);
    }

    [Fact]
    public void NeighbouringChanges_ClipTheWindow()
    {
        var invoice = Measure(MapWith(("2026-07-03", "invoice-matching"))).Where(i => i.StepId == "invoice-matching").ToList();

        Assert.Equal(2, invoice.Count);
        Assert.Equal(T("2026-07-03T00:00+03:00"), invoice[0].After.End);
        Assert.Equal(T("2026-05-04T00:00+03:00"), invoice[0].Before.Start);
        Assert.Equal(T("2026-06-03T00:00+03:00"), invoice[1].Before.Start);
    }

    [Fact]
    public void NoChanges_NoImpacts()
    {
        Assert.Empty(Measure(ProcessMapTests.Sample() with { Changes = [] }));
    }
}
