using System.Security.Cryptography;
using System.Text.Json;
using FlowSight.Cli;
using FlowSight.Core.Export;

namespace FlowSight.Tests;

public class ChangeExportTests
{
    private static readonly Lazy<string> Export = new(() => Run(800));

    private static string Run(int requests)
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "flowsight-" + Guid.NewGuid().ToString("N"))).FullName;
        DemoExporter.Run(dir, seed: 42, requests: requests);
        return dir;
    }

    private static string ChangesFile(string dir) => Path.Combine(dir, "owner", "changes.json");

    [Fact]
    public void Demo_WritesChanges()
    {
        var view = JsonSerializer.Deserialize<ChangesView>(File.ReadAllText(ChangesFile(Export.Value)), JsonDefaults.Options)!;

        var change = Assert.Single(view.Changes);
        Assert.Equal(0, change.Index);
        Assert.Equal(new DateOnly(2026, 6, 3), change.Date);
        Assert.Equal("invoice", change.Stage);
        Assert.Equal("improved", change.Verdict);
        Assert.True(change.MedianChangePercent < 0);
        Assert.NotEmpty(change.Monthly);
        Assert.True(change.BeforeStats.MedianDays > change.AfterStats.MedianDays);
    }

    [Fact]
    public void ChangesExport_IsDeterministic()
    {
        string Hash(string dir) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ChangesFile(dir))));
        Assert.Equal(Hash(Run(50)), Hash(Run(50)));
    }

    [Fact]
    public void ChangesExport_HasNoPersonIds()
    {
        Assert.DoesNotContain("A-", File.ReadAllText(ChangesFile(Export.Value)));
    }
}
