using FlowSight.Core.Calendar;
using FlowSight.Core.Journeys;
using FlowSight.Core.Model;
using FlowSight.Core.Statistics;
using FlowSight.Synthetic;

namespace FlowSight.Tests;

public class BottleneckTests
{
    private static readonly AgeingBuckets NoAgeing = new(0, 0, 0, 0);

    private static StageStats S(Stage stage, double share = 0, int wipEnd = 0, double? median = null, int wipStart = 0) =>
        new(stage, 0, wipStart, wipEnd, median, null, null, share, NoAgeing);

    private static IReadOnlyList<StageStats> All(params StageStats[] given) =>
        Stages.All.Select(s => given.FirstOrDefault(g => g.Stage == s) ?? S(s)).ToList();

    private static readonly Lazy<(IReadOnlyList<LineJourney> Lines, IReadOnlyList<Period> Periods, WorkCalendar Calendar)> Synthetic = new(() =>
    {
        var calendar = new WorkCalendar(CalendarRules.Default);
        var log = Generator.Generate(new SyntheticOptions(), calendar);
        return (JourneyBuilder.Build(log, calendar), Periods.For(log), calendar);
    });

    private static IReadOnlyList<BottleneckEntry> RankSynthetic(string periodId)
    {
        var (lines, periods, calendar) = Synthetic.Value;
        var baseline = StageStatistics.Compute(lines, periods.Single(p => p.Id == Periods.Last12Id), calendar);
        return Bottlenecks.Rank(StageStatistics.Compute(lines, periods.Single(p => p.Id == periodId), calendar), baseline);
    }

    [Fact]
    public void RanksByShareThenWip()
    {
        var ranking = Bottlenecks.Rank(All(
            S(Stage.Invoice, share: 0.4, wipEnd: 3),
            S(Stage.Purchasing, share: 0.2, wipEnd: 9),
            S(Stage.Order, share: 0.2, wipEnd: 12, wipStart: 2)), All());

        Assert.Equal([Stage.Invoice, Stage.Order, Stage.Purchasing], ranking.Take(3).Select(r => r.Stage));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], ranking.Select(r => r.Rank));
        Assert.Equal(10, ranking.Single(r => r.Stage == Stage.Order).WipGrowth);
    }

    [Fact]
    public void Pace_ComparesWithBaselineMedian()
    {
        var ranking = Bottlenecks.Rank(
            All(S(Stage.Request, median: 160), S(Stage.Approval, median: 130), S(Stage.Purchasing, median: 100),
                S(Stage.Order, median: 70), S(Stage.Invoice, median: 100)),
            All(S(Stage.Request, median: 100), S(Stage.Approval, median: 100), S(Stage.Purchasing, median: 100),
                S(Stage.Order, median: 100)));

        Pace PaceOf(Stage s) => ranking.Single(r => r.Stage == s).Pace;
        Assert.Equal(Pace.MuchSlower, PaceOf(Stage.Request));
        Assert.Equal(Pace.Slower, PaceOf(Stage.Approval));
        Assert.Equal(Pace.Normal, PaceOf(Stage.Purchasing));
        Assert.Equal(Pace.Faster, PaceOf(Stage.Order));
        Assert.Equal(Pace.Unknown, PaceOf(Stage.Invoice));   // no baseline median
        Assert.Equal(Pace.Unknown, PaceOf(Stage.Payment));   // no period median
    }

    [Theory]
    [InlineData("2026-01")]
    [InlineData("2026-03")]
    [InlineData("2026-05")]
    public void SharedQueueMonths_RankInvoiceFirst(string month)
    {
        Assert.Equal(Stage.Invoice, RankSynthetic(month)[0].Stage);
    }

    [Theory]
    [InlineData("2026-08")]
    [InlineData("2026-10")]
    public void AfterRouting_InvoiceIsNotFirstAndNotSlower(string month)
    {
        var invoice = RankSynthetic(month).Single(r => r.Stage == Stage.Invoice);
        Assert.NotEqual(1, invoice.Rank);
        Assert.True(invoice.Pace is Pace.Faster or Pace.Normal, $"pace was {invoice.Pace}");
    }
}
