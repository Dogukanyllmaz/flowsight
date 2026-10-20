using FlowSight.Core.Journeys;
using FlowSight.Core.Model;
using static FlowSight.Tests.TestCalendar;

namespace FlowSight.Tests;

public class JourneyBuilderTests
{
    private static readonly RequestLine Line = new("R1", "L1", "Site North", "Spare parts", LinePath.Goods, 1, 100, "EUR");

    private static StepEvent Ev(Stage stage, string queued, string? completed, StepOutcome? outcome = StepOutcome.Done, string line = "L1") =>
        new("R1", line, stage, stage.ToString(), "A-01", "Role", AssignmentKind.Role,
            T(queued), null, completed is null ? null : T(completed), completed is null ? null : outcome);

    private static LineJourney BuildOne(string asOf, params StepEvent[] events) =>
        JourneyBuilder.Build(new EventLog([Line], events, T(asOf)), Create()).Single();

    private static StageSpan Span(LineJourney j, Stage s) => j.Stages.Single(x => x.Stage == s);

    [Fact]
    public void OpenLine_ReportsCurrentStageAndTimeToAsOf()
    {
        var j = BuildOne("2026-10-22T12:00+03:00",
            Ev(Stage.Request, "2026-10-19T08:00+03:00", "2026-10-19T17:00+03:00"),
            Ev(Stage.Approval, "2026-10-20T08:00+03:00", "2026-10-20T12:00+03:00", StepOutcome.Approved),
            Ev(Stage.Purchasing, "2026-10-20T12:00+03:00", null));

        Assert.Equal(LineStatus.Open, j.Status);
        Assert.Equal(Stage.Purchasing, j.CurrentStage);
        Assert.Equal(7, j.Stages.Count);
        Assert.Equal(Stages.All, j.Stages.Select(s => s.Stage));
        Assert.Equal((StageState.Completed, 540d), (Span(j, Stage.Request).State, Span(j, Stage.Request).WorkingMinutes));
        Assert.Equal((StageState.Completed, 240d), (Span(j, Stage.Approval).State, Span(j, Stage.Approval).WorkingMinutes));
        Assert.Equal((StageState.InProgress, 1080d), (Span(j, Stage.Purchasing).State, Span(j, Stage.Purchasing).WorkingMinutes));
        foreach (var s in new[] { Stage.Order, Stage.GoodsReceipt, Stage.Invoice, Stage.Payment })
            Assert.Equal((StageState.NotStarted, 0d), (Span(j, s).State, Span(j, s).WorkingMinutes));
        Assert.Equal(1860, j.TotalWorkingMinutes);
        Assert.Null(Span(j, Stage.Purchasing).LeftAt);
        Assert.Equal(T("2026-10-20T12:00+03:00"), Span(j, Stage.Approval).LeftAt);
    }

    [Fact]
    public void RepeatedStage_IsUnionNotSum()
    {
        var j = BuildOne("2026-10-23T12:00+03:00",
            Ev(Stage.Approval, "2026-10-19T08:00+03:00", "2026-10-19T12:00+03:00", StepOutcome.Returned),
            Ev(Stage.Approval, "2026-10-19T10:00+03:00", "2026-10-19T14:00+03:00", StepOutcome.Approved),
            Ev(Stage.Purchasing, "2026-10-19T14:00+03:00", null));

        Assert.Equal(360, Span(j, Stage.Approval).WorkingMinutes);
    }

    [Fact]
    public void RejectedLine_HasNoCurrentStage()
    {
        var j = BuildOne("2026-10-23T12:00+03:00",
            Ev(Stage.Request, "2026-10-19T08:00+03:00", "2026-10-19T10:00+03:00"),
            Ev(Stage.Approval, "2026-10-19T10:00+03:00", "2026-10-19T12:00+03:00", StepOutcome.Rejected));

        Assert.Equal(LineStatus.Rejected, j.Status);
        Assert.Null(j.CurrentStage);
        Assert.Equal(StageState.Completed, Span(j, Stage.Approval).State);
        Assert.Equal(240, j.TotalWorkingMinutes);
    }

    [Fact]
    public void PaidLine_IsCompleted()
    {
        var day = 19;
        var events = Stages.All.Select(s =>
        {
            var d = day++;
            return Ev(s, $"2026-10-{d}T08:00+03:00", $"2026-10-{d}T10:00+03:00");
        }).ToArray();

        var j = BuildOne("2026-10-30T12:00+03:00", events);

        Assert.Equal(LineStatus.Completed, j.Status);
        Assert.Null(j.CurrentStage);
        Assert.All(j.Stages, s => Assert.Equal(StageState.Completed, s.State));
    }

    [Fact]
    public void EventsAfterAsOf_AreCutOff()
    {
        var j = BuildOne("2026-10-19T12:00+03:00",
            Ev(Stage.Request, "2026-10-19T08:00+03:00", "2026-10-19T15:00+03:00"),
            Ev(Stage.Approval, "2026-10-20T08:00+03:00", null));

        Assert.Equal((StageState.InProgress, 240d), (Span(j, Stage.Request).State, Span(j, Stage.Request).WorkingMinutes));
        Assert.Equal(StageState.NotStarted, Span(j, Stage.Approval).State);
        Assert.Equal(Stage.Request, j.CurrentStage);
        Assert.Equal(LineStatus.Open, j.Status);
    }

    [Fact]
    public void CancelledLine_ClosesStagesThatStillHadOpenSteps()
    {
        var j = BuildOne("2026-10-30T12:00+03:00",
            Ev(Stage.Purchasing, "2026-10-19T08:00+03:00", null),                                          // parallel step never finished
            Ev(Stage.Purchasing, "2026-10-19T08:00+03:00", "2026-10-20T08:00+03:00", StepOutcome.Cancelled));

        var purchasing = Span(j, Stage.Purchasing);
        Assert.Equal(LineStatus.Cancelled, j.Status);
        Assert.Equal(StageState.Completed, purchasing.State);
        Assert.Equal(T("2026-10-20T08:00+03:00"), purchasing.LeftAt);
        Assert.Equal(540, purchasing.WorkingMinutes);
    }

    [Fact]
    public void LineWithoutEvents_IsOpenAndNotStarted()
    {
        var j = BuildOne("2026-10-19T12:00+03:00");

        Assert.Equal(LineStatus.Open, j.Status);
        Assert.Null(j.CurrentStage);
        Assert.Equal(0, j.TotalWorkingMinutes);
    }
}
