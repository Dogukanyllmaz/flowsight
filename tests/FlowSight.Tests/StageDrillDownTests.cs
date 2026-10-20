using FlowSight.Core.Calendar;
using FlowSight.Core.Journeys;
using FlowSight.Core.Model;
using FlowSight.Core.Statistics;
using FlowSight.Synthetic;
using static FlowSight.Tests.TestCalendar;

namespace FlowSight.Tests;

public class StageDrillDownTests
{
    private static readonly WorkCalendar Calendar = Create();
    private static readonly Period October = new("2026-10", T("2026-10-01T00:00+03:00"), T("2026-10-23T17:00+03:00"));

    private static StepEvent Inv(string caseId, string actor, string role, string queued, string? started, string? completed) =>
        new(caseId, "L1", Stage.Invoice, "Invoice matching", actor, role, AssignmentKind.Role,
            T(queued), started is null ? null : T(started), completed is null ? null : T(completed), completed is null ? null : StepOutcome.Done);

    private static StageDetail Drill(params StepEvent[] events)
    {
        var lines = events.Select(e => e.CaseId).Distinct()
            .Select(c => new RequestLine(c, "L1", "Site North", "Spare parts", LinePath.Goods, 1, 100, "EUR")).ToList();
        var log = new EventLog(lines, events, T("2026-10-23T17:00+03:00"));
        return StageDrillDown.Compute(log, JourneyBuilder.Build(log, Calendar), Stage.Invoice, October, Calendar);
    }

    [Fact]
    public void SplitsQueueAndWork()
    {
        var d = Drill(Inv("R1", "A-01", "Clerk", "2026-10-19T08:00+03:00", "2026-10-19T10:00+03:00", "2026-10-19T11:00+03:00"));

        Assert.Equal(120, d.MedianQueueMinutes);
        Assert.Equal(60, d.MedianWorkMinutes);
        Assert.Equal("2026-10", d.PeriodId);
    }

    [Fact]
    public void MissingStart_IsNotZeroQueue()
    {
        var d = Drill(
            Inv("R1", "A-01", "Clerk", "2026-10-19T08:00+03:00", null, "2026-10-19T12:00+03:00"),
            Inv("R2", "A-01", "Clerk", "2026-10-20T08:00+03:00", "2026-10-20T10:00+03:00", "2026-10-20T11:00+03:00"));

        Assert.Equal(120, d.MedianQueueMinutes);
        Assert.Equal(60, d.MedianWorkMinutes);
        Assert.Equal(2, d.ByRole.Single().Steps);
    }

    [Fact]
    public void GroupsByRoleAndPerson()
    {
        var d = Drill(
            Inv("R1", "A-01", "Clerk", "2026-10-19T08:00+03:00", null, "2026-10-19T09:00+03:00"),
            Inv("R2", "A-01", "Clerk", "2026-10-20T08:00+03:00", null, "2026-10-20T10:00+03:00"),
            Inv("R3", "A-02", "Clerk", "2026-10-21T08:00+03:00", null, "2026-10-21T13:00+03:00"));

        var role = d.ByRole.Single();
        Assert.Equal(("Clerk", "Clerk", 3, 120d, 480d), (role.Key, role.Role, role.Steps, role.MedianMinutes!.Value, role.TotalMinutes));
        Assert.Equal(["A-02", "A-01"], d.ByPerson.Select(p => p.Key));
        Assert.Equal(90, d.ByPerson[1].MedianMinutes);
        Assert.Equal(180, d.ByPerson[1].TotalMinutes);
    }

    [Fact]
    public void SlowestOpen_AreLinesStillInStage()
    {
        var d = Drill(
            Inv("R1", "A-01", "Clerk", "2026-10-22T08:00+03:00", null, null),                          // 2 days at period end
            Inv("R2", "A-01", "Clerk", "2026-10-19T08:00+03:00", null, null),                          // 5 days
            Inv("R3", "A-01", "Clerk", "2026-10-19T08:00+03:00", null, "2026-10-19T09:00+03:00"),     // finished, moved on to payment
            Inv("R3", "A-03", "Treasury", "2026-10-19T09:00+03:00", null, null) with { Stage = Stage.Payment });

        Assert.Equal(["R2", "R1"], d.SlowestOpen.Select(c => c.CaseId));
        Assert.Equal(2700, d.SlowestOpen[0].WorkingMinutes);
        Assert.Equal(["R3"], d.SlowestCompleted.Select(c => c.CaseId));
        Assert.Equal(new AgeingBuckets(1, 1, 0, 0), d.Ageing);
    }

    [Fact]
    public void SharedQueueMonth_ShowsGroupQueueMix()
    {
        var calendar = new WorkCalendar(CalendarRules.Default);
        var log = Generator.Generate(new SyntheticOptions(), calendar);
        var lines = JourneyBuilder.Build(log, calendar);
        var periods = Periods.For(log);

        var march = StageDrillDown.Compute(log, lines, Stage.Invoice, periods.Single(p => p.Id == "2026-03"), calendar);
        var september = StageDrillDown.Compute(log, lines, Stage.Invoice, periods.Single(p => p.Id == "2026-09"), calendar);

        Assert.Equal([AssignmentKind.GroupQueue], march.AssignmentMix.Keys);
        Assert.Equal([AssignmentKind.FieldRouted], september.AssignmentMix.Keys);
        Assert.True(march.MedianQueueMinutes > 3 * september.MedianQueueMinutes);
    }
}
