using FlowSight.Core.Impact;
using FlowSight.Core.Journeys;
using FlowSight.Core.Patterns;
using FlowSight.Core.Statistics;
using static FlowSight.Core.Export.JsonDefaults;

namespace FlowSight.Core.Export;

// Process-owner contract. May carry roles and pseudonymous actor ids (A-01…), never anything else about a person.

public sealed record PeriodInfo(string Id, DateTimeOffset Start, DateTimeOffset End);

public sealed record TrendPoint(string PeriodId, int Paid, double? MedianLeadDays, double? P90LeadDays);

public sealed record PeriodsView(IReadOnlyList<PeriodInfo> Periods, IReadOnlyList<TrendPoint> Trend);

public sealed record StageStatsView(
    string Stage,
    int Rank,
    int Throughput,
    int WipStart,
    int WipEnd,
    double? MedianDays,
    double? P90Days,
    double? MedianInclOpenDays,
    double SharePercent,
    AgeingBuckets Ageing,
    string Pace);

public sealed record OverviewView(string PeriodId, DateTimeOffset Start, DateTimeOffset End, IReadOnlyList<StageStatsView> Stages);

public sealed record GroupTimeView(string Key, string Role, int Steps, double? MedianDays, double TotalDays);

public sealed record SlowCaseView(string CaseId, string LineId, string ItemCategory, double WorkingDays);

public sealed record StageDetailView(
    string Stage,
    string PeriodId,
    double? MedianQueueDays,
    double? MedianWorkDays,
    IReadOnlyList<GroupTimeView> ByRole,
    IReadOnlyList<GroupTimeView> ByPerson,
    IReadOnlyDictionary<string, int> AssignmentMix,
    AgeingBuckets Ageing,
    IReadOnlyList<SlowCaseView> SlowestOpen,
    IReadOnlyList<SlowCaseView> SlowestCompleted);

public sealed record EvidenceView(string Code, IReadOnlyDictionary<string, double> Values);

/// <param name="ExpectedSavingDaysPerLine">Working days the symptom adds per affected line; null when not estimable.</param>
public sealed record CardView(
    string Id,
    string Pattern,
    string Stage,
    string StepId,
    string StepName,
    string? Segment,
    string MapProperty,
    IReadOnlyList<EvidenceView> Evidence,
    string ProposedChange,
    double? ExpectedSavingDaysPerLine,
    int AffectedLines,
    double? ExpectedSavingDaysTotal);

public sealed record CardsView(string PeriodId, IReadOnlyList<CardView> Cards, IReadOnlyList<string> InsufficientData, IReadOnlyList<string> UnmappedActivities);

public sealed record ImpactWindowView(DateTimeOffset Start, DateTimeOffset End);

public sealed record ImpactStatsView(int Steps, double? MedianDays, double? P90Days, double? MedianQueueDays, double AverageWaiting);

public sealed record MonthlyPointView(string PeriodId, double? MedianDays, int Steps);

/// <param name="Index">Position in the map's change list; used in the URL.</param>
public sealed record ChangeImpactView(
    int Index,
    DateOnly Date,
    string StepId,
    string StepName,
    string? Stage,
    string Description,
    ImpactWindowView Before,
    ImpactWindowView After,
    ImpactStatsView BeforeStats,
    ImpactStatsView AfterStats,
    double? MedianChangePercent,
    string Verdict,
    IReadOnlyList<MonthlyPointView> Monthly);

public sealed record ChangesView(IReadOnlyList<ChangeImpactView> Changes);

public static class OwnerViews
{
    public static ChangesView Changes(IReadOnlyList<ChangeImpact> impacts, int minutesPerDay)
    {
        ImpactStatsView Stats(ImpactStats s) => new(s.Steps, Days(s.MedianStepMinutes, minutesPerDay), Days(s.P90StepMinutes, minutesPerDay),
            Days(s.MedianQueueMinutes, minutesPerDay), s.AverageWaiting);

        return new ChangesView(impacts.Select((i, index) => new ChangeImpactView(
            index, i.Change.Date, i.StepId, i.StepName, i.Stage is { } st ? Name(st) : null, i.Change.Description,
            new ImpactWindowView(i.Before.Start, i.Before.End), new ImpactWindowView(i.After.Start, i.After.End),
            Stats(i.BeforeStats), Stats(i.AfterStats), i.MedianChangePercent, Name(i.Verdict),
            i.Monthly.Select(m => new MonthlyPointView(m.PeriodId, Days(m.MedianStepMinutes, minutesPerDay), m.Steps)).ToList())).ToList());
    }

    public static CardsView Cards(Period period, PatternResult result, int minutesPerDay) =>
        new(period.Id, result.Cards.Select(c => new CardView(
                c.Id, c.Pattern, Name(c.Stage), c.StepId, c.StepName, c.Segment, c.MapProperty,
                c.Evidence.Select(e => new EvidenceView(e.Code, e.Values)).ToList(),
                c.ProposedChange,
                Days(c.ExpectedSavingMinutesPerLine, minutesPerDay),
                c.AffectedLines,
                c.ExpectedSavingMinutesPerLine is { } m ? Days(m * c.AffectedLines, minutesPerDay) : null)).ToList(),
            result.InsufficientData, result.UnmappedActivities);

    public static PeriodsView Periods(IReadOnlyList<Period> periods, IReadOnlyList<LineJourney> lines, int minutesPerDay)
    {
        var paid = lines.Where(l => l.Status == LineStatus.Completed).ToList();
        var trend = periods.Where(p => p.Id != Statistics.Periods.Last12Id).Select(p =>
        {
            var totals = paid
                .Where(l => l.Stages[^1].LeftAt is { } t && t >= p.Start && t < p.End)
                .Select(l => l.TotalWorkingMinutes).ToList();
            return new TrendPoint(p.Id, totals.Count,
                Days(Percentiles.Median(totals), minutesPerDay), Days(Percentiles.Percentile(totals, 0.9), minutesPerDay));
        }).ToList();

        return new PeriodsView(periods.Select(p => new PeriodInfo(p.Id, p.Start, p.End)).ToList(), trend);
    }

    public static OverviewView Overview(Period period, IReadOnlyList<StageStats> stats, IReadOnlyList<BottleneckEntry> ranking, int minutesPerDay) =>
        new(period.Id, period.Start, period.End, stats.Select(s =>
        {
            var entry = ranking.Single(r => r.Stage == s.Stage);
            return new StageStatsView(
                Name(s.Stage), entry.Rank, s.Throughput, s.WipStart, s.WipEnd,
                Days(s.MedianMinutes, minutesPerDay), Days(s.P90Minutes, minutesPerDay), Days(s.MedianInclOpenMinutes, minutesPerDay),
                Math.Round(s.ShareOfLeadTime * 100, 1), s.Ageing, Name(entry.Pace));
        }).ToList());

    public static StageDetailView StageDetail(StageDetail d, int minutesPerDay)
    {
        GroupTimeView Group(GroupTime g) => new(g.Key, g.Role, g.Steps, Days(g.MedianMinutes, minutesPerDay), Days(g.TotalMinutes, minutesPerDay));
        SlowCaseView Slow(SlowCase c) => new(c.CaseId, c.LineId, c.ItemCategory, Days(c.WorkingMinutes, minutesPerDay));

        return new StageDetailView(
            Name(d.Stage), d.PeriodId,
            Days(d.MedianQueueMinutes, minutesPerDay), Days(d.MedianWorkMinutes, minutesPerDay),
            d.ByRole.Select(Group).ToList(), d.ByPerson.Select(Group).ToList(),
            d.AssignmentMix.ToDictionary(kv => Name(kv.Key), kv => kv.Value),
            d.Ageing,
            d.SlowestOpen.Select(Slow).ToList(), d.SlowestCompleted.Select(Slow).ToList());
    }

    private static double? Days(double? minutes, int minutesPerDay) => minutes is null ? null : Days(minutes.Value, minutesPerDay);

    private static double Days(double minutes, int minutesPerDay) => Math.Round(minutes / minutesPerDay, 1);
}
