namespace FlowSight.Core.Statistics;

public static class Percentiles
{
    /// <summary>Inclusive linear interpolation (P90 of 1…10 = 9.1). Null for an empty list.</summary>
    public static double? Percentile(IReadOnlyList<double> values, double p)
    {
        if (values.Count == 0) return null;
        var sorted = values.Order().ToArray();
        var position = p * (sorted.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }

    public static double? Median(IReadOnlyList<double> values) => Percentile(values, 0.5);

    /// <summary>
    /// Median duration including spans that have not finished yet (censored). Uses the Kaplan–Meier estimator:
    /// the first observed duration at which the share still waiting falls to one half or below. Null when it never does.
    /// </summary>
    public static double? KaplanMeierMedian(IReadOnlyList<(double Minutes, bool Observed)> spans)
    {
        var atRisk = spans.Count;
        var survival = 1.0;
        // At equal durations, finished spans are processed before censored ones.
        foreach (var group in spans.GroupBy(s => s.Minutes).OrderBy(g => g.Key))
        {
            var deaths = group.Count(s => s.Observed);
            if (deaths > 0)
            {
                survival *= (double)(atRisk - deaths) / atRisk;
                if (survival <= 0.5) return group.Key;
            }
            atRisk -= group.Count();
        }
        return null;
    }
}
