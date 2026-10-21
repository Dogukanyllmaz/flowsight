using FlowSight.Core.Journeys;
using FlowSight.Core.Model;
using static FlowSight.Tests.JourneyFixtures;

namespace FlowSight.Tests;

public class SimilarityTests
{
    private static readonly double[] Hundreds = [100, 200, 300, 400, 500, 600, 700, 800, 900, 1000];

    private static List<LineJourney> CompletedSimilar(string category = "Spare parts") =>
        Hundreds.Select((m, i) => Line($"S{i}", LineStatus.Completed, null, m, caseId: $"C{i}", category: category,
            minutesPerStage: new Dictionary<Stage, double> { [Stage.Invoice] = m })).ToList();

    [Fact]
    public void PercentileRank_CountsStrictlyBelow()
    {
        Assert.Equal(80.0, Similarity.PercentileRank(850, Hundreds));
        Assert.Equal(0.0, Similarity.PercentileRank(100, Hundreds));
    }

    [Fact]
    public void PercentileRank_NullBelowTenSamples()
    {
        Assert.Null(Similarity.PercentileRank(850, Hundreds[..9]));
    }

    [Fact]
    public void TypicalStageMinutes_IsMedianOfCompletedSimilar()
    {
        var all = CompletedSimilar();
        var line = Line("X", LineStatus.Open, Stage.Invoice, 900, caseId: "X");
        all.Add(line);

        Assert.Equal(550, Similarity.TypicalStageMinutes(Stage.Invoice, line, all));
        Assert.Equal(80.0, Similarity.SlowerThanPercent(line, all));
    }

    [Fact]
    public void Similarity_IgnoresOtherCategoriesAndOpenLines()
    {
        var all = CompletedSimilar(category: "IT hardware");
        all.AddRange(Hundreds.Select((m, i) => Line($"O{i}", LineStatus.Open, Stage.Invoice, m, caseId: $"O{i}")));
        var line = Line("X", LineStatus.Open, Stage.Invoice, 900, caseId: "X");
        all.Add(line);

        Assert.Null(Similarity.TypicalStageMinutes(Stage.Invoice, line, all));
        Assert.Null(Similarity.SlowerThanPercent(line, all));
    }

    [Fact]
    public void Similarity_ExcludesTheLineItself()
    {
        var all = CompletedSimilar()[..9];
        var line = Line("X", LineStatus.Completed, null, 900, caseId: "X");
        all.Add(line);

        Assert.Null(Similarity.SlowerThanPercent(line, all));
    }

    [Fact]
    public void Index_MatchesTheDirectComparison()
    {
        var calendar = new FlowSight.Core.Calendar.WorkCalendar(FlowSight.Core.Calendar.CalendarRules.Default);
        var log = FlowSight.Synthetic.Generator.Generate(new FlowSight.Synthetic.SyntheticOptions(Requests: 400), calendar);
        var lines = JourneyBuilder.Build(log, calendar);
        var index = new SimilarityIndex(lines);

        foreach (var line in lines)
        {
            Assert.Equal(Similarity.SlowerThanPercent(line, lines), index.SlowerThanPercent(line));
            foreach (var stage in Stages.All)
                Assert.Equal(Similarity.TypicalStageMinutes(stage, line, lines), index.TypicalStageMinutes(stage, line));
        }
    }
}
