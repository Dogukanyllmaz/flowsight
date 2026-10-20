using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using FlowSight.Cli;
using FlowSight.Core.Export;

namespace FlowSight.Tests;

public class OwnerExportTests
{
    private static readonly Lazy<string> Export = new(() => Run());

    private static string Run()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "flowsight-" + Guid.NewGuid().ToString("N"))).FullName;
        DemoExporter.Run(dir, seed: 42, requests: 50);
        return dir;
    }

    private static string Owner(params string[] parts) => Path.Combine([Export.Value, "owner", .. parts]);

    private static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonDefaults.Options)!;

    [Fact]
    public void Demo_WritesOwnerFiles()
    {
        var periods = Read<PeriodsView>(Owner("periods.json"));

        Assert.True(File.Exists(Owner("overview", "last12.json")));
        Assert.True(File.Exists(Owner("stages", "last12", "invoice.json")));
        Assert.Equal("last12", periods.Periods[^1].Id);
        Assert.Equal(periods.Periods.Count, Directory.GetFiles(Owner("overview"), "*.json").Length);
        Assert.Equal(periods.Periods.Count * 7, Directory.GetFiles(Owner("stages"), "*.json", SearchOption.AllDirectories).Length);
        Assert.Equal(periods.Periods.Count - 1, periods.Trend.Count);
    }

    [Fact]
    public void OwnerFiles_OnlyPseudonymousActors()
    {
        foreach (var file in Directory.GetFiles(Owner("stages"), "*.json", SearchOption.AllDirectories))
            Assert.All(Read<StageDetailView>(file).ByPerson, p => Assert.Matches(@"^A-\d{2}$", p.Key));
    }

    [Fact]
    public void RequesterFiles_StillHaveNoActors()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(Export.Value, "requests"), "*.json"))
        {
            var json = File.ReadAllText(file);
            Assert.DoesNotContain("A-", json);
            Assert.DoesNotContain("\"role\"", json);
        }
    }

    [Fact]
    public void Overview_StagesInStageOrderWithRanks()
    {
        var overview = Read<OverviewView>(Owner("overview", "last12.json"));

        Assert.Equal(["request", "approval", "purchasing", "order", "goodsReceipt", "invoice", "payment"], overview.Stages.Select(s => s.Stage));
        Assert.Equal(Enumerable.Range(1, 7), overview.Stages.Select(s => s.Rank).Order());
        Assert.Matches(new Regex("^(unknown|faster|normal|slower|muchSlower)$"), overview.Stages[0].Pace);
    }

    [Fact]
    public void Last12_HasNoPaceComparisonWithItself()
    {
        Assert.All(Read<OverviewView>(Owner("overview", "last12.json")).Stages, s => Assert.Equal("unknown", s.Pace));
    }

    [Fact]
    public void OwnerExport_IsDeterministic()
    {
        Assert.Equal(Hashes(Owner()), Hashes(Path.Combine(Run(), "owner")));
    }

    [Fact]
    public void OwnerExport_KeepsUnrelatedFiles()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "flowsight-" + Guid.NewGuid().ToString("N"))).FullName;
        var notes = Path.Combine(Directory.CreateDirectory(Path.Combine(dir, "owner")).FullName, "notes.txt");
        File.WriteAllText(notes, "keep me");

        DemoExporter.Run(dir, seed: 42, requests: 5);

        Assert.True(File.Exists(notes));
    }

    private static List<string> Hashes(string dir) =>
        Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(dir, f) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))))
            .Order(StringComparer.Ordinal)
            .ToList();
}
