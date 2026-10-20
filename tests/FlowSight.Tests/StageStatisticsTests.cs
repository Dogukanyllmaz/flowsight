using FlowSight.Core.Calendar;
using FlowSight.Core.Journeys;
using FlowSight.Core.Model;
using FlowSight.Core.Statistics;
using FlowSight.Synthetic;
using static FlowSight.Tests.TestCalendar;

namespace FlowSight.Tests;

public class StageStatisticsTests
{
    private static readonly WorkCalendar Calendar = Create();
    private static readonly Period October = new("2026-10", T("2026-10-01T00:00+03:00"), T("2026-10-23T17:00+03:00"));

    /// <summary>A line whose given stages carry explicit entry/exit times and minutes; other stages are not started.</summary>
    private static LineJourney Lj(string id, LineStatus status, double total, params (Stage Stage, string Entered, string? Left, double Minutes)[] spans)
    {
        var stages = Stages.All.Select(s =>
        {
            var match = spans.Where(x => x.Stage == s).ToList();
            if (match.Count == 0) return new StageSpan(s, StageState.NotStarted, null, null, 0, 0);
            var (_, entered, left, minutes) = match[0];
            return new StageSpan(s, left is null ? StageState.InProgress : StageState.Completed, T(entered), left is null ? null : T(left), minutes, minutes / 540d);
        }).ToList();
        var current = status == LineStatus.Open ? stages.LastOrDefault(s => s.State == StageState.InProgress)?.Stage : null;
        return new LineJourney(new RequestLine(id, "L1", "Site North", "Spare parts", LinePath.Goods, 1, 100, "EUR"), stages, current, status, total, total / 540d);
    }

    private static StageStats Of(IReadOnlyList<StageStats> stats, Stage stage) => stats.Single(s => s.Stage == stage);

    [Fact]
    public void Throughput_CountsSpansLeavingInPeriod()
    {
        var stats = StageStatistics.Compute([
            Lj("R1", LineStatus.Open, 0, (Stage.Approval, "2026-09-28T08:00+03:00", "2026-10-02T08:00+03:00", 100)),
            Lj("R2", LineStatus.Open, 0, (Stage.Approval, "2026-10-05T08:00+03:00", "2026-10-06T08:00+03:00", 300)),
            Lj("R3", LineStatus.Open, 0, (Stage.Approval, "2026-09-01T08:00+03:00", "2026-09-02T08:00+03:00", 999))], October, Calendar);

        var approval = Of(stats, Stage.Approval);
        Assert.Equal(2, approval.Throughput);
        Assert.Equal(200, approval.MedianMinutes);
        Assert.Equal(7, stats.Count);
    }

    [Fact]
    public void Wip_CountsSpansOpenAtStartAndEnd()
    {
        var stats = StageStatistics.Compute([
            Lj("R1", LineStatus.Open, 0, (Stage.Invoice, "2026-09-20T08:00+03:00", null, 0)),                         // open throughout
            Lj("R2", LineStatus.Open, 0, (Stage.Invoice, "2026-09-25T08:00+03:00", "2026-10-05T08:00+03:00", 0)),     // only at start
            Lj("R3", LineStatus.Open, 0, (Stage.Invoice, "2026-10-10T08:00+03:00", null, 0))], October, Calendar);    // only at end

        var invoice = Of(stats, Stage.Invoice);
        Assert.Equal(2, invoice.WipStart);
        Assert.Equal(2, invoice.WipEnd);
    }

    [Fact]
    public void RejectedLine_LeavesWipWhenItEnds()
    {
        var stats = StageStatistics.Compute([
            Lj("R1", LineStatus.Rejected, 0, (Stage.Approval, "2026-09-25T08:00+03:00", "2026-10-07T08:00+03:00", 0))], October, Calendar);

        var approval = Of(stats, Stage.Approval);
        Assert.Equal(1, approval.WipStart);
        Assert.Equal(0, approval.WipEnd);
    }

    [Fact]
    public void Ageing_BucketsWipByWorkingDaysInStage()
    {
        var stats = StageStatistics.Compute([
            Lj("R1", LineStatus.Open, 0, (Stage.Order, "2026-10-23T08:00+03:00", null, 0)),   // 1 working day
            Lj("R2", LineStatus.Open, 0, (Stage.Order, "2026-10-20T08:00+03:00", null, 0)),   // 4
            Lj("R3", LineStatus.Open, 0, (Stage.Order, "2026-10-16T08:00+03:00", null, 0)),   // 7
            Lj("R4", LineStatus.Open, 0, (Stage.Order, "2026-10-10T08:00+03:00", null, 0))],  // 12
            October, Calendar);

        Assert.Equal(new AgeingBuckets(1, 1, 1, 1), Of(stats, Stage.Order).Ageing);
    }

    [Fact]
    public void ShareOfLeadTime_UsesLinesPaidInPeriod()
    {
        var stats = StageStatistics.Compute([
            Lj("R1", LineStatus.Completed, 1000,
                (Stage.Invoice, "2026-10-01T08:00+03:00", "2026-10-02T08:00+03:00", 300),
                (Stage.Payment, "2026-10-02T08:00+03:00", "2026-10-09T08:00+03:00", 700)),
            Lj("R2", LineStatus.Completed, 1000,
                (Stage.Invoice, "2026-10-01T08:00+03:00", "2026-10-02T08:00+03:00", 100),
                (Stage.Payment, "2026-10-02T08:00+03:00", "2026-10-09T08:00+03:00", 900)),
            Lj("R3", LineStatus.Completed, 5000,                                                // paid before the period
                (Stage.Invoice, "2026-09-01T08:00+03:00", "2026-09-02T08:00+03:00", 5000),
                (Stage.Payment, "2026-09-02T08:00+03:00", "2026-09-09T08:00+03:00", 0))], October, Calendar);

        Assert.Equal(0.2, Of(stats, Stage.Invoice).ShareOfLeadTime, 6);
        Assert.Equal(0.8, Of(stats, Stage.Payment).ShareOfLeadTime, 6);
    }

    [Fact]
    public void MedianInclOpen_CensorsSpansStillInStage()
    {
        // Entered in October: two finished after 1 day, three still open after 4+ days → median is censored-aware.
        var stats = StageStatistics.Compute([
            Lj("R1", LineStatus.Open, 0, (Stage.Purchasing, "2026-10-19T08:00+03:00", "2026-10-19T17:00+03:00", 540)),
            Lj("R2", LineStatus.Open, 0, (Stage.Purchasing, "2026-10-19T08:00+03:00", "2026-10-19T17:00+03:00", 540)),
            Lj("R3", LineStatus.Open, 0, (Stage.Purchasing, "2026-10-20T08:00+03:00", null, 0)),
            Lj("R4", LineStatus.Open, 0, (Stage.Purchasing, "2026-10-20T08:00+03:00", null, 0)),
            Lj("R5", LineStatus.Open, 0, (Stage.Purchasing, "2026-10-20T08:00+03:00", null, 0))], October, Calendar);

        var purchasing = Of(stats, Stage.Purchasing);
        Assert.Equal(540, purchasing.MedianMinutes);
        Assert.Null(purchasing.MedianInclOpenMinutes);
    }

    [Fact]
    public void EmptyPeriod_GivesNullStatistics()
    {
        var stats = StageStatistics.Compute([], October, Calendar);

        Assert.Equal(7, stats.Count);
        Assert.All(stats, s =>
        {
            Assert.Null(s.MedianMinutes);
            Assert.Null(s.P90Minutes);
            Assert.Null(s.MedianInclOpenMinutes);
            Assert.Equal(0, s.ShareOfLeadTime);
            Assert.Equal(new AgeingBuckets(0, 0, 0, 0), s.Ageing);
        });
    }

    [Fact]
    public void Periods_AreFullMonthsPlusLast12()
    {
        var log = Generator.Generate(new SyntheticOptions(Requests: 300), new WorkCalendar(CalendarRules.Default));

        var periods = Periods.For(log);

        Assert.Equal("2025-11", periods[0].Id);
        Assert.Equal("2026-10", periods[^2].Id);
        Assert.Equal("last12", periods[^1].Id);
        Assert.Equal(log.AsOf, periods[^1].End);
        Assert.Equal(log.AsOf.AddMonths(-12), periods[^1].Start);
        Assert.Equal(T("2026-10-01T00:00+03:00"), periods[^2].Start);
        Assert.Equal(T("2026-11-01T00:00+03:00"), periods[^2].End);
    }

    [Fact]
    public void Last12_WipEqualsCurrentStageCounts()
    {
        var calendar = new WorkCalendar(CalendarRules.Default);
        var log = Generator.Generate(new SyntheticOptions(Requests: 300), calendar);
        var lines = JourneyBuilder.Build(log, calendar);

        var stats = StageStatistics.Compute(lines, Periods.Last12(log.AsOf), calendar);

        foreach (var s in stats)
            Assert.Equal(lines.Count(l => l.Status == LineStatus.Open && l.CurrentStage == s.Stage), s.WipEnd);
    }
}
