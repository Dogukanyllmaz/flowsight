using FlowSight.Core.Calendar;
using FlowSight.Core.Model;
using FlowSight.Synthetic;

namespace FlowSight.Tests;

/// <summary>Ground truth for the injected shared-invoice-queue scenario.</summary>
public class ScenarioTests
{
    private static readonly SyntheticOptions Options = new();
    private static readonly WorkCalendar Calendar = new(CalendarRules.Default);
    private static readonly Lazy<EventLog> Log = new(() => Generator.Generate(Options, Calendar));
    private static readonly DateTimeOffset Change =
        new(Options.ChangeDate.ToDateTime(TimeOnly.MinValue), CalendarRules.Default.UtcOffset);

    private static IEnumerable<StepEvent> Invoices(bool beforeChange) =>
        Log.Value.Events.Where(e => e.Stage == Stage.Invoice && (e.QueuedAt < Change) == beforeChange);

    private static double MedianMinutes(IEnumerable<StepEvent> events)
    {
        var values = events.Where(e => e.CompletedAt is not null)
            .Select(e => Calendar.WorkingMinutes(e.QueuedAt, e.CompletedAt!.Value)).Order().ToList();
        return values[values.Count / 2];
    }

    [Fact]
    public void ChangeDate_IsSevenMonthsAfterStart()
    {
        Assert.Equal(new DateOnly(2026, 6, 3), Options.ChangeDate);
    }

    [Fact]
    public void InvoicesBeforeChange_GoToSharedQueue()
    {
        var before = Invoices(beforeChange: true).ToList();
        Assert.NotEmpty(before);
        Assert.All(before, e =>
        {
            Assert.Equal(AssignmentKind.GroupQueue, e.Assignment);
            Assert.Equal(6, e.CandidateCount);
        });
    }

    [Fact]
    public void InvoicesAfterChange_AreRouted()
    {
        var siteOf = Log.Value.Lines.ToDictionary(l => (l.CaseId, l.LineId), l => l.SiteId);
        var after = Invoices(beforeChange: false).ToList();
        Assert.NotEmpty(after);
        Assert.All(after, e =>
        {
            Assert.Equal(AssignmentKind.FieldRouted, e.Assignment);
            Assert.Equal(1, e.CandidateCount);
            Assert.Equal(Catalog.RoutedClerk(siteOf[(e.CaseId, e.LineId)]), e.ActorId);
        });
    }

    [Fact]
    public void InvoiceWait_DropsAfterChange()
    {
        Assert.True(MedianMinutes(Invoices(true)) >= 3 * MedianMinutes(Invoices(false)));
    }

    [Fact]
    public void OtherStages_AreUnchangedByScenario()
    {
        var purchasing = Log.Value.Events.Where(e => e.Stage == Stage.Purchasing).ToList();
        var ratio = MedianMinutes(purchasing.Where(e => e.QueuedAt < Change)) / MedianMinutes(purchasing.Where(e => e.QueuedAt >= Change));
        Assert.InRange(ratio, 0.7, 1.4);
    }
}
