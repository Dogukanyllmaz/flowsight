using static FlowSight.Tests.TestCalendar;

namespace FlowSight.Tests;

public class CalendarTests
{
    private readonly FlowSight.Core.Calendar.WorkCalendar _calendar = Create();

    [Theory]
    [InlineData("2026-10-19T09:00+03:00", "2026-10-19T10:30+03:00", 90)]   // Monday, inside hours
    [InlineData("2026-10-24T16:00+03:00", "2026-10-26T09:00+03:00", 120)]  // Sat 16-17 + Mon 08-09, Sunday off
    [InlineData("2026-10-27T16:00+03:00", "2026-10-30T09:00+03:00", 420)]  // Tue 60 + Wed half-day 300 + Thu holiday 0 + Fri 60
    [InlineData("2026-10-25T10:00+03:00", "2026-10-25T15:00+03:00", 0)]    // Sunday
    [InlineData("2026-10-19T06:00+03:00", "2026-10-19T08:30+03:00", 30)]   // before start of day
    [InlineData("2026-10-19T12:00+03:00", "2026-10-19T09:00+03:00", 0)]    // to before from
    public void WorkingMinutes_CountsOnlyWorkingTime(string from, string to, double expected)
    {
        Assert.Equal(expected, _calendar.WorkingMinutes(T(from), T(to)));
    }

    [Fact]
    public void WorkingMinutes_NormalisesOtherOffsets()
    {
        Assert.Equal(90, _calendar.WorkingMinutes(T("2026-10-19T06:00Z"), T("2026-10-19T07:30Z")));
    }

    [Fact]
    public void WorkingMinutes_UsesSiteOverride()
    {
        var from = T("2026-10-24T10:00+03:00");
        var to = T("2026-10-24T12:00+03:00");
        Assert.Equal(120, _calendar.WorkingMinutes(from, to));
        Assert.Equal(0, _calendar.WorkingMinutes(from, to, "S2"));
    }

    [Theory]
    [InlineData("2026-10-24T16:30+03:00", 60, "2026-10-26T08:30+03:00")]   // 30 on Sat, 30 on Mon
    [InlineData("2026-10-25T12:00+03:00", 10, "2026-10-26T08:10+03:00")]   // starts on Sunday
    [InlineData("2026-10-28T12:00+03:00", 120, "2026-10-30T09:00+03:00")]  // 60 Wed until 13:00, Thu holiday, 60 Fri
    public void AddWorkingMinutes_SkipsNonWorkingTime(string from, double minutes, string expected)
    {
        Assert.Equal(T(expected), _calendar.AddWorkingMinutes(T(from), minutes));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(539)]
    [InlineData(540)]
    [InlineData(2000)]
    public void AddWorkingMinutes_IsInverseOfWorkingMinutes(double minutes)
    {
        var from = T("2026-10-19T09:00+03:00");
        Assert.Equal(minutes, _calendar.WorkingMinutes(from, _calendar.AddWorkingMinutes(from, minutes)), 6);
    }

    [Fact]
    public void MinutesPerDay_IsNineHours()
    {
        Assert.Equal(540, _calendar.MinutesPerDay);
    }
}
