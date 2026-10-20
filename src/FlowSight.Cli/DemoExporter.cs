using System.Text;
using System.Text.Json;
using FlowSight.Core.Calendar;
using FlowSight.Core.Export;
using FlowSight.Core.Journeys;
using FlowSight.Core.Model;
using FlowSight.Core.Statistics;
using FlowSight.Synthetic;

namespace FlowSight.Cli;

/// <summary>Writes the static demo data the web app reads: meta, request index and one file per request.</summary>
public static class DemoExporter
{
    /// <summary>Demo calendar: Sunday off, Saturday working, fixed-date public holidays (eve of 29 October from 13:00).</summary>
    public static WorkCalendar DemoCalendar()
    {
        var holidays = new List<Holiday>();
        foreach (var year in new[] { 2025, 2026, 2027 })
        {
            foreach (var (month, day) in new[] { (1, 1), (4, 23), (5, 1), (5, 19), (7, 15), (8, 30), (10, 29) })
                holidays.Add(new Holiday(new DateOnly(year, month, day)));
            holidays.Add(new Holiday(new DateOnly(year, 10, 28), new TimeOnly(13, 0)));
        }
        return new WorkCalendar(CalendarRules.Default with { Holidays = holidays });
    }

    public static void Run(string outDir, int seed, int requests)
    {
        var calendar = DemoCalendar();
        var log = Generator.Generate(new SyntheticOptions(Seed: seed, Requests: requests), calendar);
        var lines = JourneyBuilder.Build(log, calendar);
        var journeys = RequestAggregator.Build(lines);

        // Comparisons only ever use lines with the same path and category, so hand each request just that group.
        var groups = lines
            .GroupBy(l => (l.Line.Path, l.Line.ItemCategory))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<LineJourney>)g.ToList());

        // Remove only files this exporter writes; anything else in the folder is left alone.
        var requestDir = Directory.CreateDirectory(Path.Combine(outDir, "requests")).FullName;
        foreach (var stale in Directory.GetFiles(requestDir, "PR-*.json").Append(Path.Combine(requestDir, "index.json")))
            File.Delete(stale);

        foreach (var journey in journeys)
        {
            var peers = journey.Lines.SelectMany(l => groups[(l.Line.Path, l.Line.ItemCategory)]).Distinct().ToList();
            Write(Path.Combine(requestDir, $"{journey.CaseId}.json"),
                RequestViewBuilder.Build(journey, peers, calendar.MinutesPerDay, log.AsOf));
        }

        Write(Path.Combine(requestDir, "index.json"), journeys
            .Select(j => new RequestIndexEntry(j.CaseId, JsonDefaults.Name(j.Status), j.CurrentStage is { } s ? JsonDefaults.Name(s) : null))
            .ToList());

        Write(Path.Combine(outDir, "meta.json"), new DemoMeta(
            log.AsOf, seed, requests, calendar.MinutesPerDay, Stages.All.Select(JsonDefaults.Name).ToList()));

        WriteOwner(Path.Combine(outDir, "owner"), log, lines, calendar);
    }

    /// <summary>Process-owner statistics: periods and trend, one overview per period, one drill-down per period and stage.</summary>
    private static void WriteOwner(string ownerDir, EventLog log, IReadOnlyList<LineJourney> lines, WorkCalendar calendar)
    {
        var overviewDir = Directory.CreateDirectory(Path.Combine(ownerDir, "overview")).FullName;
        var stagesDir = Directory.CreateDirectory(Path.Combine(ownerDir, "stages")).FullName;

        // Remove only files this exporter writes.
        foreach (var stale in Directory.GetFiles(overviewDir, "*.json")
                     .Concat(Directory.GetDirectories(stagesDir).SelectMany(d => Directory.GetFiles(d, "*.json")))
                     .Append(Path.Combine(ownerDir, "periods.json")))
            File.Delete(stale);

        var periods = Periods.For(log);
        var baseline = StageStatistics.Compute(lines, periods[^1], calendar);
        Write(Path.Combine(ownerDir, "periods.json"), OwnerViews.Periods(periods, lines, calendar.MinutesPerDay));

        foreach (var period in periods)
        {
            var stats = period.Id == Periods.Last12Id ? baseline : StageStatistics.Compute(lines, period, calendar);
            Write(Path.Combine(overviewDir, $"{period.Id}.json"),
                // last12 is the baseline itself, so it gets no pace comparison rather than a meaningless "normal".
                OwnerViews.Overview(period, stats, Bottlenecks.Rank(stats, period.Id == Periods.Last12Id ? [] : baseline), calendar.MinutesPerDay));

            var periodDir = Directory.CreateDirectory(Path.Combine(stagesDir, period.Id)).FullName;
            foreach (var stage in Stages.All)
                Write(Path.Combine(periodDir, $"{JsonDefaults.Name(stage)}.json"),
                    OwnerViews.StageDetail(StageDrillDown.Compute(log, lines, stage, period, calendar), calendar.MinutesPerDay));
        }
    }

    private static void Write<T>(string path, T value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonDefaults.Options), new UTF8Encoding(false));
}
