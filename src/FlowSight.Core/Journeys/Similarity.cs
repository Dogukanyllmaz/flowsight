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
