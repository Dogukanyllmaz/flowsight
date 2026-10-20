using FlowSight.Core.Model;

namespace FlowSight.Core.Statistics;

/// <summary>How a stage's median in a period compares with its median over the last 12 months.</summary>
public enum Pace { Unknown, Faster, Normal, Slower, MuchSlower }

public sealed record BottleneckEntry(Stage Stage, int Rank, double ShareOfLeadTime, int WipEnd, int WipGrowth, Pace Pace);

public static class Bottlenecks
{
    public const double SlowerRatio = 1.2;
    public const double MuchSlowerRatio = 1.5;
    public const double FasterRatio = 0.8;

    /// <summary>Stages ordered by their share of lead time, then by work waiting at period end.</summary>
    public static IReadOnlyList<BottleneckEntry> Rank(IReadOnlyList<StageStats> period, IReadOnlyList<StageStats> baseline) =>
        period
            .OrderByDescending(s => s.ShareOfLeadTime)
            .ThenByDescending(s => s.WipEnd)
            .ThenBy(s => s.Stage)
            .Select((s, i) => new BottleneckEntry(
                s.Stage,
                i + 1,
                s.ShareOfLeadTime,
                s.WipEnd,
                s.WipEnd - s.WipStart,
                PaceOf(s.MedianMinutes, baseline.FirstOrDefault(b => b.Stage == s.Stage)?.MedianMinutes)))
            .ToList();

    public static Pace PaceOf(double? median, double? baselineMedian)
    {
        if (median is null || baselineMedian is not > 0) return Pace.Unknown;
        var ratio = median.Value / baselineMedian.Value;
        return ratio >= MuchSlowerRatio ? Pace.MuchSlower
            : ratio >= SlowerRatio ? Pace.Slower
            : ratio < FasterRatio ? Pace.Faster
            : Pace.Normal;
    }
}
