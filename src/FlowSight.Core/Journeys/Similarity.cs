using FlowSight.Core.Model;

namespace FlowSight.Core.Journeys;

/// <summary>
/// Compares a line with similar ones: same path and item category, completed, excluding itself.
/// Every comparison needs at least <see cref="MinimumSample"/> similar lines; below that it returns null.
/// </summary>
public static class Similarity
{
    public const int MinimumSample = 10;

    /// <summary>Share of the population strictly below <paramref name="value"/>, in percent with one decimal.</summary>
    public static double? PercentileRank(double value, IReadOnlyList<double> population)
    {
        if (population.Count < MinimumSample) return null;
        var below = population.Count(p => p < value);
        return Math.Round(100.0 * below / population.Count, 1);
    }

    /// <summary>Median working minutes similar lines spent in <paramref name="stage"/>.</summary>
    public static double? TypicalStageMinutes(Stage stage, LineJourney line, IReadOnlyList<LineJourney> all)
    {
        var values = Similar(line, all).Select(l => l.Stages.Single(s => s.Stage == stage).WorkingMinutes).ToList();
        return values.Count < MinimumSample ? null : Median(values);
    }

    /// <summary>How many percent of similar lines finished faster than this line has taken so far.</summary>
    public static double? SlowerThanPercent(LineJourney line, IReadOnlyList<LineJourney> all) =>
        PercentileRank(line.TotalWorkingMinutes, Similar(line, all).Select(l => l.TotalWorkingMinutes).ToList());

    private static IEnumerable<LineJourney> Similar(LineJourney line, IReadOnlyList<LineJourney> all) =>
        all.Where(l =>
            l.Status == LineStatus.Completed &&
            l.Line.Path == line.Line.Path &&
            l.Line.ItemCategory == line.Line.ItemCategory &&
            !(l.Line.CaseId == line.Line.CaseId && l.Line.LineId == line.Line.LineId));

    private static double Median(List<double> values)
    {
        values.Sort();
        var mid = values.Count / 2;
        return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2;
    }
}

/// <summary>
/// The same comparisons as <see cref="Similarity"/> for many lines at once: completed lines are grouped by path and
/// category and their durations sorted once, instead of filtering every line for every comparison.
/// </summary>
public sealed class SimilarityIndex
{
    private sealed record Group(HashSet<(string, string)> Keys, double[] Totals, double[][] StageMinutes);

    private readonly Dictionary<(LinePath, string), Group> _groups;

    public SimilarityIndex(IReadOnlyList<LineJourney> all)
    {
        _groups = all
            .Where(l => l.Status == LineStatus.Completed)
            .GroupBy(l => (l.Line.Path, l.Line.ItemCategory))
            .ToDictionary(g => g.Key, g => new Group(
                g.Select(l => (l.Line.CaseId, l.Line.LineId)).ToHashSet(),
                g.Select(l => l.TotalWorkingMinutes).Order().ToArray(),
                Stages.All.Select(s => g.Select(l => l.Stages[(int)s - 1].WorkingMinutes).Order().ToArray()).ToArray()));
    }

    public double? TypicalStageMinutes(Stage stage, LineJourney line)
    {
        if (!_groups.TryGetValue((line.Line.Path, line.Line.ItemCategory), out var group)) return null;
        var self = IsMember(group, line) ? line.Stages[(int)stage - 1].WorkingMinutes : (double?)null;
        return MedianWithout(group.StageMinutes[(int)stage - 1], self);
    }

    public double? SlowerThanPercent(LineJourney line)
    {
        if (!_groups.TryGetValue((line.Line.Path, line.Line.ItemCategory), out var group)) return null;
        var count = group.Totals.Length - (IsMember(group, line) ? 1 : 0);
        if (count < Similarity.MinimumSample) return null;
        // The line's own total is never strictly below itself, so excluding it does not change the count below.
        var below = LowerBound(group.Totals, line.TotalWorkingMinutes);
        return Math.Round(100.0 * below / count, 1);
    }

    private static bool IsMember(Group group, LineJourney line) =>
        line.Status == LineStatus.Completed && group.Keys.Contains((line.Line.CaseId, line.Line.LineId));

    /// <summary>Median of a sorted array with one occurrence of <paramref name="excluded"/> left out.</summary>
    private static double? MedianWithout(double[] sorted, double? excluded)
    {
        var skip = excluded is { } e ? LowerBound(sorted, e) : sorted.Length;
        var count = sorted.Length - (excluded is null ? 0 : 1);
        if (count < Similarity.MinimumSample) return null;
        double At(int i) => i < skip ? sorted[i] : sorted[i + 1];
        var mid = count / 2;
        return count % 2 == 1 ? At(mid) : (At(mid - 1) + At(mid)) / 2;
    }

    private static int LowerBound(double[] sorted, double value)
    {
        int lo = 0, hi = sorted.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (sorted[mid] < value) lo = mid + 1; else hi = mid;
        }
        return lo;
    }
}
