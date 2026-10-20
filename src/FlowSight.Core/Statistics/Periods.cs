using FlowSight.Core.Model;

namespace FlowSight.Core.Statistics;

/// <summary>A reporting window <c>[Start, End)</c>. Ids are <c>yyyy-MM</c> for months and <c>last12</c>.</summary>
public sealed record Period(string Id, DateTimeOffset Start, DateTimeOffset End);

public static class Periods
{
    public const string Last12Id = "last12";

    /// <summary>Every full calendar month from the first event up to the as-of date, oldest first, then <c>last12</c>.</summary>
    public static IReadOnlyList<Period> For(EventLog log)
    {
        var offset = log.AsOf.Offset;
        var result = new List<Period>();
        if (log.Events.Count > 0)
        {
            var first = log.Events.Min(e => e.QueuedAt).ToOffset(offset);
            var month = new DateTimeOffset(first.Year, first.Month, 1, 0, 0, 0, offset);
            while (month.AddMonths(1) <= log.AsOf)
            {
                result.Add(new Period($"{month:yyyy-MM}", month, month.AddMonths(1)));
                month = month.AddMonths(1);
            }
        }
        result.Add(Last12(log.AsOf));
        return result;
    }

    /// <summary>The twelve months up to <paramref name="asOf"/>; its end is "now".</summary>
    public static Period Last12(DateTimeOffset asOf) => new(Last12Id, asOf.AddMonths(-12), asOf);
}
