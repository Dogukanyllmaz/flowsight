using System.Security.Cryptography;
using System.Text.Json;
using FlowSight.Cli;
using FlowSight.Core.Export;

namespace FlowSight.Tests;

public class CardExportTests
{
    private static readonly Lazy<string> Export = new(() => Run(800));

    private static string Run(int requests)
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "flowsight-" + Guid.NewGuid().ToString("N"))).FullName;
        DemoExporter.Run(dir, seed: 42, requests: requests);
        return dir;
    }

    private static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonDefaults.Options)!;

    private static IEnumerable<RequestView> Requests() =>
        Directory.GetFiles(Path.Combine(Export.Value, "requests"), "PR-*.json").Select(Read<RequestView>);

    [Fact]
    public void Demo_WritesCardsPerPeriod()
    {
        var periods = Read<PeriodsView>(Path.Combine(Export.Value, "owner", "periods.json")).Periods;

        Assert.All(periods, p => Assert.True(File.Exists(Path.Combine(Export.Value, "owner", "cards", $"{p.Id}.json"))));
    }

    [Fact]
    public void MarchCards_IncludeSharedQueue()
    {
        var cards = Read<CardsView>(Path.Combine(Export.Value, "owner", "cards", "2026-03.json"));

        var card = cards.Cards.Single(c => c.Id == "sharedQueueSearch:invoice-matching");
        Assert.Equal("invoice", card.Stage);
        Assert.Equal("routeByAttribute", card.ProposedChange);
        Assert.True(card.ExpectedSavingDaysPerLine > 0);
        // The total comes from unrounded minutes, so allow for the per-line rounding to 0.1 day.
        var approx = card.ExpectedSavingDaysPerLine!.Value * card.AffectedLines;
        Assert.InRange(card.ExpectedSavingDaysTotal!.Value, approx - 0.05 * card.AffectedLines, approx + 0.05 * card.AffectedLines);
    }

    [Fact]
    public void RequestFiles_CarryReasonsWithoutActors()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(Export.Value, "requests"), "PR-*.json"))
        {
            var json = File.ReadAllText(file);
            Assert.DoesNotContain("A-", json);
            Assert.DoesNotContain("\"role\"", json);
        }
        Assert.Contains(Requests(), r => r.Status == "open" && r.WaitingReasons.Any(w => w.Code is "sharedQueue" or "waitingForBatch"));
    }

    [Fact]
    public void ClosedRequests_HaveEmptyReasons()
    {
        Assert.All(Requests().Where(r => r.Status != "open"), r =>
        {
            Assert.Empty(r.WaitingReasons);
            Assert.All(r.Lines, l => Assert.Empty(l.WaitingReasons));
        });
    }

    [Fact]
    public void CardsExport_IsDeterministic()
    {
        Assert.Equal(Hashes(Run(50)), Hashes(Run(50)));
    }

    private static List<string> Hashes(string dir) =>
        Directory.GetFiles(Path.Combine(dir, "owner", "cards"), "*.json")
            .Select(f => Path.GetFileName(f) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))))
            .Order(StringComparer.Ordinal)
            .ToList();
}
