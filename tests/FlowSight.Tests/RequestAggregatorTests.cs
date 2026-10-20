using FlowSight.Core.Journeys;
using FlowSight.Core.Model;
using static FlowSight.Tests.JourneyFixtures;

namespace FlowSight.Tests;

public class RequestAggregatorTests
{
    [Fact]
    public void CurrentStage_IsLeastAdvancedOpenLine()
    {
        var r = RequestAggregator.Build([
            Line("L1", LineStatus.Open, Stage.Purchasing, 100),
            Line("L2", LineStatus.Open, Stage.Invoice, 300)]).Single();

        Assert.Equal(Stage.Purchasing, r.CurrentStage);
        Assert.Equal(RequestStatus.Open, r.Status);
        Assert.Equal(300, r.TotalWorkingMinutes);
    }

    [Fact]
    public void RejectedLine_DoesNotHoldBackRequest()
    {
        var r = RequestAggregator.Build([
            Line("L1", LineStatus.Rejected, Stage.Approval),
            Line("L2", LineStatus.Open, Stage.Invoice)]).Single();

        Assert.Equal(Stage.Invoice, r.CurrentStage);
        Assert.Equal(RequestStatus.Open, r.Status);
    }

    [Fact]
    public void AllLinesPaid_RequestCompleted()
    {
        var r = RequestAggregator.Build([
            Line("L1", LineStatus.Completed, null),
            Line("L2", LineStatus.Completed, null)]).Single();

        Assert.Equal(RequestStatus.Completed, r.Status);
        Assert.Null(r.CurrentStage);
    }

    [Fact]
    public void AllLinesRejected_RequestRejected()
    {
        var r = RequestAggregator.Build([Line("L1", LineStatus.Rejected, Stage.Approval)]).Single();

        Assert.Equal(RequestStatus.Rejected, r.Status);
    }

    [Fact]
    public void RequestStages_AlwaysSevenInOrder()
    {
        var r = RequestAggregator.Build([
            Line("L1", LineStatus.Open, Stage.Purchasing, minutesPerStage: new Dictionary<Stage, double> { [Stage.Request] = 100, [Stage.Purchasing] = 50 }),
            Line("L2", LineStatus.Open, Stage.Invoice, minutesPerStage: new Dictionary<Stage, double> { [Stage.Request] = 200, [Stage.Purchasing] = 20 })]).Single();

        Assert.Equal(Stages.All, r.Stages.Select(s => s.Stage));
        Assert.Equal(200, r.Stages[0].WorkingMinutes);
        Assert.Equal(StageState.InProgress, r.Stages.Single(s => s.Stage == Stage.Purchasing).State);
        Assert.Equal(StageState.InProgress, r.Stages.Single(s => s.Stage == Stage.Invoice).State);
        Assert.Equal(StageState.NotStarted, r.Stages.Single(s => s.Stage == Stage.Payment).State);
    }

    [Fact]
    public void InProgressStage_CountsOnlyLinesStillThere()
    {
        var r = RequestAggregator.Build([
            Line("L1", LineStatus.Open, Stage.Purchasing, minutesPerStage: new Dictionary<Stage, double> { [Stage.Purchasing] = 100 }),
            Line("L2", LineStatus.Open, Stage.Invoice, minutesPerStage: new Dictionary<Stage, double> { [Stage.Purchasing] = 500 })]).Single();

        Assert.Equal(100, r.Stages.Single(s => s.Stage == Stage.Purchasing).WorkingMinutes);
    }

    [Fact]
    public void GroupsLinesByCase()
    {
        var requests = RequestAggregator.Build([
            Line("L1", LineStatus.Open, Stage.Order, caseId: "R2"),
            Line("L1", LineStatus.Open, Stage.Order, caseId: "R1")]);

        Assert.Equal(["R1", "R2"], requests.Select(r => r.CaseId));
    }
}
