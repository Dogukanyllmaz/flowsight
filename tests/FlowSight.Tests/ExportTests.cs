using System.Security.Cryptography;
using System.Text.Json;
using FlowSight.Cli;
using FlowSight.Core.Calendar;
using FlowSight.Core.Export;
using FlowSight.Core.Journeys;
using FlowSight.Core.Model;
using FlowSight.Synthetic;
using static FlowSight.Tests.JourneyFixtures;

namespace FlowSight.Tests;

public class ExportTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 10, 20, 8, 0, 0, TimeSpan.FromHours(3));

    private static RequestView ViewOf(params LineJourney[] lines) =>
        RequestViewBuilder.Build(RequestAggregator.Build(lines).Single(), lines, 540, AsOf);

    [Fact]
    public void RequestView_HasNoActorInformation()
    {
        var calendar = new WorkCalendar(CalendarRules.Default);
        var log = Generator.Generate(new SyntheticOptions(Requests: 40), calendar);
        var lines = JourneyBuilder.Build(log, calendar);
        var requests = RequestAggregator.Build(lines);

        foreach (var request in requests)
        {
            var json = JsonSerializer.Serialize(RequestViewBuilder.Build(request, lines, 540, log.AsOf), JsonDefaults.Options);
            Assert.DoesNotContain("actorId", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"role\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("A-", json);
        }
    }

    [Fact]
    public void RequestView_ConvertsMinutesToDays()
    {
        var view = ViewOf(Line("L1", LineStatus.Open, Stage.Purchasing, 1350,
            minutesPerStage: new Dictionary<Stage, double> { [Stage.Purchasing] = 1080 }));

        Assert.Equal(2.0, view.Stages.Single(s => s.Stage == "purchasing").WorkingDays);
        Assert.Equal(2.5, view.TotalWorkingDays);
    }

    [Fact]
    public void RequestView_UsesCamelCaseAndStringEnums()
    {
        var json = JsonSerializer.Serialize(ViewOf(Line("L1", LineStatus.Open, Stage.GoodsReceipt)), JsonDefaults.Options);

        Assert.Contains("\"currentStage\":\"goodsReceipt\"", json);
        Assert.Contains("\"status\":\"open\"", json);
        Assert.Contains("\"slowerThanPercent\":null", json);
    }

    [Fact]
    public void Demo_WritesMetaIndexAndOneFilePerRequest()
    {
        var dir = TempDir();
        DemoExporter.Run(dir, seed: 42, requests: 50);

        var index = JsonSerializer.Deserialize<List<RequestIndexEntry>>(File.ReadAllText(Path.Combine(dir, "requests", "index.json")), JsonDefaults.Options)!;
        Assert.True(File.Exists(Path.Combine(dir, "meta.json")));
        Assert.Equal(50, index.Count);
        Assert.Equal(50, Directory.GetFiles(Path.Combine(dir, "requests"), "PR-*.json").Length);
        Assert.All(index, e => Assert.True(File.Exists(Path.Combine(dir, "requests", $"{e.CaseId}.json"))));
    }

    [Fact]
    public void Demo_IsByteIdenticalForSameSeed()
    {
        var a = TempDir();
        var b = TempDir();
        DemoExporter.Run(a, seed: 42, requests: 50);
        DemoExporter.Run(b, seed: 42, requests: 50);

        Assert.Equal(Hashes(a), Hashes(b));
    }

    private static string TempDir() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "flowsight-" + Guid.NewGuid().ToString("N"))).FullName;

    private static List<string> Hashes(string dir) =>
        Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(f => Path.GetRelativePath(dir, f) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))))
            .ToList();
}
