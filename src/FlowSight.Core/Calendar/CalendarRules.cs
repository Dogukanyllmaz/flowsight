namespace FlowSight.Core.Calendar;

/// <summary>A public holiday. <paramref name="OffFrom"/> null means the whole day is off;
/// otherwise work stops at that time (e.g. the eve of a holiday from 13:00).</summary>
public sealed record Holiday(DateOnly Date, TimeOnly? OffFrom = null);

public sealed record CalendarRules(
    TimeOnly DayStart,
    TimeOnly DayEnd,
    IReadOnlySet<DayOfWeek> NonWorkingDays,
    IReadOnlyList<Holiday> Holidays,
    TimeSpan UtcOffset)
{
    /// <summary>08:00–17:00, Sunday off, no holidays, UTC+03:00.</summary>
    public static CalendarRules Default { get; } = new(
        new TimeOnly(8, 0),
        new TimeOnly(17, 0),
        new HashSet<DayOfWeek> { DayOfWeek.Sunday },
        [],
        TimeSpan.FromHours(3));

    public int MinutesPerDay => (int)(DayEnd - DayStart).TotalMinutes;
}
