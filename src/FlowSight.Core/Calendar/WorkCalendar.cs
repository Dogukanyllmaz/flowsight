namespace FlowSight.Core.Calendar;

public interface IWorkCalendar
{
    /// <summary>Working minutes between two instants; 0 when <paramref name="to"/> is not after <paramref name="from"/>.</summary>
    double WorkingMinutes(DateTimeOffset from, DateTimeOffset to, string? siteId = null);

    /// <summary>The instant reached after spending <paramref name="minutes"/> working minutes from <paramref name="from"/>.</summary>
    DateTimeOffset AddWorkingMinutes(DateTimeOffset from, double minutes, string? siteId = null);

    /// <summary>Length of a full working day, used to convert minutes to working days.</summary>
    int MinutesPerDay { get; }
}

public sealed class WorkCalendar(
    CalendarRules rules,
    IReadOnlyDictionary<string, CalendarRules>? siteOverrides = null) : IWorkCalendar
{
    private const int MaxDays = 20 * 366;

    public int MinutesPerDay => rules.MinutesPerDay;

    public double WorkingMinutes(DateTimeOffset from, DateTimeOffset to, string? siteId = null)
    {
        if (to <= from) return 0;
        var r = RulesFor(siteId);
        var start = from.ToOffset(r.UtcOffset);
        var end = to.ToOffset(r.UtcOffset);

        double total = 0;
        for (var day = DateOnly.FromDateTime(start.DateTime); day <= DateOnly.FromDateTime(end.DateTime); day = day.AddDays(1))
        {
            if (Window(day, r) is not var (open, close)) continue;
            var s = start > open ? start : open;
            var e = end < close ? end : close;
            if (e > s) total += (e - s).TotalMinutes;
        }
        return total;
    }

    public DateTimeOffset AddWorkingMinutes(DateTimeOffset from, double minutes, string? siteId = null)
    {
        var r = RulesFor(siteId);
        var current = from.ToOffset(r.UtcOffset);
        var remaining = Math.Max(0, minutes);

        for (var i = 0; i < MaxDays; i++)
        {
            var day = DateOnly.FromDateTime(current.DateTime);
            if (Window(day, r) is var (open, close) && current < close)
            {
                var s = current > open ? current : open;
                var available = (close - s).TotalMinutes;
                if (remaining <= available) return s.AddMinutes(remaining);
                remaining -= available;
            }
            current = new DateTimeOffset(day.AddDays(1).ToDateTime(TimeOnly.MinValue), r.UtcOffset);
        }
        throw new InvalidOperationException("The calendar has no working time in the next 20 years.");
    }

    private CalendarRules RulesFor(string? siteId) =>
        siteId is not null && siteOverrides is not null && siteOverrides.TryGetValue(siteId, out var site) ? site : rules;

    private static (DateTimeOffset Open, DateTimeOffset Close)? Window(DateOnly day, CalendarRules r)
    {
        if (r.NonWorkingDays.Contains(day.DayOfWeek)) return null;

        var close = r.DayEnd;
        foreach (var holiday in r.Holidays)
        {
            if (holiday.Date != day) continue;
            if (holiday.OffFrom is null) return null;
            if (holiday.OffFrom.Value < close) close = holiday.OffFrom.Value;
        }
        if (close <= r.DayStart) return null;

        return (new DateTimeOffset(day.ToDateTime(r.DayStart), r.UtcOffset),
                new DateTimeOffset(day.ToDateTime(close), r.UtcOffset));
    }
}
