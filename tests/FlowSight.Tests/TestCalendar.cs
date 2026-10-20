using FlowSight.Core.Calendar;

namespace FlowSight.Tests;

internal static class TestCalendar
{
    public static WorkCalendar Create()
    {
        var rules = CalendarRules.Default with
        {
            Holidays =
            [
                new Holiday(new DateOnly(2026, 10, 28), new TimeOnly(13, 0)),
                new Holiday(new DateOnly(2026, 10, 29))
            ]
        };
        var s2 = rules with { NonWorkingDays = new HashSet<DayOfWeek> { DayOfWeek.Saturday, DayOfWeek.Sunday } };
        return new WorkCalendar(rules, new Dictionary<string, CalendarRules> { ["S2"] = s2 });
    }

    public static DateTimeOffset T(string value) => DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
