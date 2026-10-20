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

    private static IEnumerable<LineJourney> Similar(string category, double from, double step) =>
        Enumerable.Range(0, 10).Select(i => Line($"S{i}", LineStatus.Completed, null, from + i * step, caseId: $"{category}-{i}", category: category,
            minutesPerStage: new Dictionary<Stage, double> { [Stage.Invoice] = from + i * step }));

    [Fact]
    public void RejectedRequest_HasNoComparison()
    {
        var request = Line("L1", LineStatus.Rejected, Stage.Approval, 900);
        var all = Similar("Spare parts", 100, 100).Append(request).ToList();

        var view = RequestViewBuilder.Build(RequestAggregator.Build([request]).Single(), all, 540, AsOf);

        Assert.Null(view.SlowerThanPercent);
    }

    [Fact]
    public void OpenRequest_FasterThanAllSimilar_HasNoComparison()
    {
        var request = Line("L1", LineStatus.Open, Stage.Approval, 50);
        var all = Similar("Spare parts", 100, 100).Append(request).ToList();

        var view = RequestViewBuilder.Build(RequestAggregator.Build([request]).Single(), all, 540, AsOf);

        Assert.Null(view.SlowerThanPercent);
    }

    [Fact]
    public void RequestStageTypical_ComesFromTheLineDefiningTheStageTime()
    {
        var sparePart = Line("L1", LineStatus.Open, Stage.Invoice, 5000, caseId: "R", category: "Spare parts",
            minutesPerStage: new Dictionary<Stage, double> { [Stage.Invoice] = 900 });
        var hardware = Line("L2", LineStatus.Open, Stage.Order, 6000, caseId: "R", category: "IT hardware");
        var all = Similar("Spare parts", 100, 100).Concat(Similar("IT hardware", 1000, 1000)).Append(sparePart).Append(hardware).ToList();

        var view = RequestViewBuilder.Build(RequestAggregator.Build([sparePart, hardware]).Single(), all, 540, AsOf);

        var invoice = view.Stages.Single(s => s.Stage == "invoice");
        Assert.Equal(1.7, invoice.WorkingDays);          // 900 / 540
        Assert.Equal(1.0, invoice.TypicalWorkingDays);   // median 550 of spare parts, not 5500 of IT hardware
    }

    [Fact]
    public void Demo_KeepsUnrelatedFilesInTheRequestsFolder()
    {
        var dir = TempDir();
        var requests = Directory.CreateDirectory(Path.Combine(dir, "requests")).FullName;
        File.WriteAllText(Path.Combine(requests, "notes.txt"), "keep me");
        File.WriteAllText(Path.Combine(requests, "PR-999999.json"), "{}");

        DemoExporter.Run(dir, seed: 42, requests: 5);

        Assert.True(File.Exists(Path.Combine(requests, "notes.txt")));
        Assert.False(File.Exists(Path.Combine(requests, "PR-999999.json")));
    }

    private static string TempDir() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "flowsight-" + Guid.NewGuid().ToString("N"))).FullName;

    private static List<string> Hashes(string dir) =>
        Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(f => Path.GetRelativePath(dir, f) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))))
            .ToList();
}
