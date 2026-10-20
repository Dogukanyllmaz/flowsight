using System.Text.Json;
using FlowSight.Core.Calendar;
using FlowSight.Core.Model;
using FlowSight.Synthetic;

namespace FlowSight.Tests;

public class GeneratorTests
{
    private static readonly WorkCalendar Calendar = new(CalendarRules.Default);
    private static readonly Lazy<EventLog> Full = new(() => Generator.Generate(new SyntheticOptions(), Calendar));

    private static EventLog Small(int seed = 42) => Generator.Generate(new SyntheticOptions(Seed: seed, Requests: 300), Calendar);

    [Fact]
    public void SameSeed_ProducesIdenticalLog()
    {
        Assert.Equal(JsonSerializer.Serialize(Small()), JsonSerializer.Serialize(Small()));
    }

    [Fact]
    public void DifferentSeed_ProducesDifferentLog()
    {
        Assert.NotEqual(JsonSerializer.Serialize(Small(42)), JsonSerializer.Serialize(Small(7)));
    }

    [Fact]
    public void GeneratesRequestedNumberOfRequests()
    {
        var log = Small();
        Assert.Equal(300, log.Lines.Select(l => l.CaseId).Distinct().Count());
        Assert.Matches(@"^PR-\d{6}$", log.Lines[0].CaseId);
    }

    [Fact]
    public void AllTimestamps_FallInWorkingTime()
    {
        var times = Full.Value.Events.SelectMany(e => new[] { e.QueuedAt, e.StartedAt, e.CompletedAt }).OfType<DateTimeOffset>();
        Assert.All(times, t =>
        {
            var local = t.ToOffset(CalendarRules.Default.UtcOffset);
            var atDayEnd = TimeOnly.FromDateTime(local.DateTime) == CalendarRules.Default.DayEnd;
            Assert.True(atDayEnd || Calendar.WorkingMinutes(t, t.AddMinutes(1)) == 1, $"{t:o} is outside working time");
        });
    }

    [Fact]
    public void StagesFollowInOrderPerLine()
    {
        foreach (var line in Full.Value.Events.GroupBy(e => (e.CaseId, e.LineId)))
        {
            var byStage = line.GroupBy(e => e.Stage).OrderBy(g => g.Key).ToList();
            for (var i = 1; i < byStage.Count; i++)
            {
                var previousEnd = byStage[i - 1].Max(e => e.CompletedAt);
                Assert.NotNull(previousEnd);
                Assert.True(byStage[i].Min(e => e.QueuedAt) >= previousEnd, $"{line.Key} {byStage[i].Key} starts before {byStage[i - 1].Key} ends");
            }
        }
    }

    [Fact]
    public void NothingAfterAsOf()
    {
        var log = Full.Value;
        Assert.All(log.Events, e =>
        {
            Assert.True(e.QueuedAt <= log.AsOf);
            Assert.True(e.CompletedAt is null || e.CompletedAt <= log.AsOf);
        });
    }

    [Fact]
    public void HasRealisticMix()
    {
        var log = Full.Value;
        var lines = log.Events.GroupBy(e => (e.CaseId, e.LineId)).ToList();
        var rejectedShare = lines.Count(l => l.Any(e => e.Outcome == StepOutcome.Rejected)) / (double)lines.Count;
        var open = lines.Count(l => l.Any(e => e.CompletedAt is null));

        Assert.Equal(2000, log.Lines.Select(l => l.CaseId).Distinct().Count());
        Assert.InRange(rejectedShare, 0.02, 0.06);
        Assert.True(open > 0);
        Assert.Contains(log.Lines, l => l.Path == LinePath.Goods);
        Assert.Contains(log.Lines, l => l.Path == LinePath.Service);
        Assert.Contains(lines, l => l.Any(e => e.Stage == Stage.Payment && e.Outcome == StepOutcome.Done));
    }
}
