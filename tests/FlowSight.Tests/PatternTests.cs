using FlowSight.Core.Calendar;
using FlowSight.Core.Journeys;
using FlowSight.Core.Model;
using FlowSight.Core.Patterns;
using FlowSight.Core.Statistics;
using FlowSight.Synthetic;

namespace FlowSight.Tests;

public class PatternTests
{
    private static readonly WorkCalendar Calendar = new(CalendarRules.Default);

    private static readonly Lazy<(EventLog Log, IReadOnlyList<LineJourney> Lines, IReadOnlyList<Period> Periods)> Data = new(() =>
    {
        var log = Generator.Generate(new SyntheticOptions(), Calendar);
        return (log, JourneyBuilder.Build(log, Calendar), Periods.For(log));
    });

    private static PatternResult Detect(string periodId, EventLog? log = null)
    {
        var (defaultLog, lines, periods) = Data.Value;
        return PatternLibrary.Detect(log ?? defaultLog, lines, ProcessMapTests.Sample(), periods.Single(p => p.Id == periodId), Calendar);
    }

    private static IEnumerable<string> Ids(PatternResult r) => r.Cards.Select(c => c.Id);

    [Fact]
    public void SharedQueue_DetectedBeforeChange()
    {
        var card = Detect("2026-03").Cards.Single(c => c.Id == "sharedQueueSearch:invoice-matching");

        Assert.Equal("routeByAttribute", card.ProposedChange);
        Assert.Equal(Stage.Invoice, card.Stage);
        Assert.True(card.Evidence.Single(e => e.Code == "queueShare").Values["value"] >= 0.7);
        Assert.True(card.ExpectedSavingMinutesPerLine > 0);
    }

    [Fact]
    public void SharedQueue_NotDetectedAfterChange()
    {
        Assert.DoesNotContain("sharedQueueSearch:invoice-matching", Ids(Detect("2026-09")));
    }

    [Fact]
    public void SerialApprovals_Detected()
    {
        var card = Detect("2026-03").Cards.Single(c => c.Id == "serialApprovalStack:it-approval");

        Assert.Equal("parallelOrThresholdApproval", card.ProposedChange);
        Assert.Equal(3, card.Evidence.Single(e => e.Code == "approvalsInChain").Values["value"]);
    }

    [Theory]
    [InlineData("2026-02")]
    [InlineData("2026-08")]
    public void SinglePointOfFailure_DetectedInAbsenceMonth(string month)
    {
        var card = Detect(month).Cards.Single(c => c.Id == "singlePointOfFailure:payment-run");
        Assert.Equal("assignToRoleWithDelegation", card.ProposedChange);
        // One 19-day window falls in each of these months.
        Assert.Equal(19, card.Evidence.Single(e => e.Code == "absenceDays").Values["value"]);
    }

    [Fact]
    public void SinglePointOfFailure_NotDetectedWhenAStandInCovers()
    {
        Assert.DoesNotContain("singlePointOfFailure:payment-run", Ids(Detect("2026-04")));
    }

    [Fact]
    public void Rework_DetectedForTheAffectedCategory()
    {
        var card = Detect("2026-03").Cards.Single(c => c.Id == "reworkLoop:sourcing");

        Assert.Equal("validateAtEntry", card.ProposedChange);
        Assert.Equal("Maintenance service", card.Segment);
    }

    [Fact]
    public void BatchWaiting_Detected()
    {
        var card = Detect("2026-03").Cards.Single(c => c.Id == "batchWaiting:payment-run");

        Assert.Equal("dailyProcessingWithSla", card.ProposedChange);
        Assert.Equal((double)DayOfWeek.Friday, card.Evidence.Single(e => e.Code == "weekdayShare").Values["weekday"]);
    }

    [Fact]
    public void ExceptionQueue_Detected()
    {
        var card = Detect("2026-09").Cards.Single(c => c.Id == "exceptionQueue:invoice-matching");
        Assert.Equal("toleranceRulesUpstream", card.ProposedChange);
    }

    [Fact]
    public void ChangeOnTheFirstOfNextMonth_DoesNotApplyToThisMonth()
    {
        var yaml = ProcessMapTests.SampleYaml().Replace("date: 2026-06-03", "date: 2026-04-01");
        var map = FlowSight.Core.ProcessMaps.ProcessMapYaml.Parse(yaml);
        var (log, lines, periods) = Data.Value;

        var march = PatternLibrary.Detect(log, lines, map, periods.Single(p => p.Id == "2026-03"), Calendar);

        Assert.Contains("sharedQueueSearch:invoice-matching", Ids(march));
    }

    [Fact]
    public void UncomputableEvidence_IsLeftOutNotZero()
    {
        var log = Data.Value.Log;
        var noStarts = log with { Events = log.Events.Select(e => e.Stage == Stage.Payment ? e with { StartedAt = null } : e).ToList() };

        var card = Detect("2026-03", noStarts).Cards.Single(c => c.Id == "batchWaiting:payment-run");

        Assert.DoesNotContain(card.Evidence, e => e.Code == "medianAfterPickUp");
        Assert.Null(card.ExpectedSavingMinutesPerLine);
    }

    [Fact]
    public void NoFalsePositives()
    {
        var ids = Ids(Detect("2026-09")).ToList();
        foreach (var step in new[] { "create-order", "goods-receipt", "create-request", "service-acceptance" })
            Assert.DoesNotContain(ids, id => id.EndsWith(":" + step));
    }

    [Fact]
    public void MissingAbsences_IsInsufficientData()
    {
        var result = Detect("2026-02", Data.Value.Log with { Absences = [] });

        Assert.Contains("singlePointOfFailure:payment-run", result.InsufficientData);
        Assert.DoesNotContain("singlePointOfFailure:payment-run", Ids(result));
    }

    [Fact]
    public void MissingLinks_IsInsufficientData()
    {
        var result = Detect("2026-09", Data.Value.Log with { Links = null });

        Assert.Contains("exceptionQueue:invoice-matching", result.InsufficientData);
    }

    [Fact]
    public void UnmappedActivity_IsReportedNotFatal()
    {
        var log = Data.Value.Log;
        var extra = log.Events.First(e => e.Stage == Stage.Order && e.CompletedAt is { } c && c.Month == 3 && c.Year == 2026) with { Activity = "Print order" };

        var result = Detect("2026-03", log with { Events = [.. log.Events, extra] });

        Assert.Equal(["Print order"], result.UnmappedActivities);
        Assert.NotEmpty(result.Cards);
    }

    [Fact]
    public void FewEvents_NoFinding()
    {
        var log = Generator.Generate(new SyntheticOptions(Requests: 5), Calendar);
        var lines = JourneyBuilder.Build(log, Calendar);

        var result = PatternLibrary.Detect(log, lines, ProcessMapTests.Sample(), Periods.Last12(log.AsOf), Calendar);

        Assert.Empty(result.Cards);
        Assert.Empty(result.InsufficientData);
    }

    [Fact]
    public void Cards_SortedByTotalExpectedSaving()
    {
        var totals = Detect("2026-03").Cards.Select(c => (c.ExpectedSavingMinutesPerLine ?? 0) * c.AffectedLines).ToList();

        Assert.True(totals.Count >= 4);
        Assert.Equal(totals.OrderByDescending(t => t), totals);
    }
}
