using NetworkSpeed.Core.Quota;

namespace NetworkSpeed.Core.Tests.Quota;

public class QuotaPeriodTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Fact]
    public void Daily_IsToday()
    {
        var w = QuotaPeriod.Daily(new DateTime(2026, 11, 12, 10, 0, 0));
        Assert.Equal((QuotaKind.Daily, D(2026, 11, 12), D(2026, 11, 12), "daily:2026-11-12"), (w.Kind, w.From, w.To, w.Key));
    }

    [Fact]
    public void Monthly_Start1_IsCalendarMonth()
    {
        var w = QuotaPeriod.Monthly(new DateTime(2026, 11, 12, 10, 0, 0), 1);
        Assert.Equal((D(2026, 11, 1), D(2026, 11, 30), "monthly:2026-11-01"), (w.From, w.To, w.Key));
    }

    [Fact]
    public void Monthly_BeforeStartDay_UsesPreviousMonth()
    {
        var w = QuotaPeriod.Monthly(new DateTime(2026, 11, 12), 15);
        Assert.Equal((D(2026, 10, 15), D(2026, 11, 14)), (w.From, w.To));
    }

    [Fact]
    public void Monthly_Start31_InFebruary_ClampsToMonthEnd()
    {
        var before = QuotaPeriod.Monthly(new DateTime(2027, 2, 20), 31);
        Assert.Equal((D(2027, 1, 31), D(2027, 2, 27)), (before.From, before.To));
        var on = QuotaPeriod.Monthly(new DateTime(2027, 2, 28), 31);
        Assert.Equal((D(2027, 2, 28), D(2027, 3, 30)), (on.From, on.To));
    }

    [Fact]
    public void Monthly_AtMidnightOfStartDay_NewPeriod()
    {
        var w = QuotaPeriod.Monthly(new DateTime(2026, 11, 15, 0, 0, 0), 15);
        Assert.Equal((D(2026, 11, 15), D(2026, 12, 14)), (w.From, w.To));
    }

    [Fact]
    public void Monthly_YearBoundary()
    {
        var w = QuotaPeriod.Monthly(new DateTime(2027, 1, 5), 20);
        Assert.Equal((D(2026, 12, 20), D(2027, 1, 19)), (w.From, w.To));
    }

    [Fact]
    public void Monthly_AlwaysContainsToday_AndNeverLongerThan31Days()
    {
        for (var startDay = 1; startDay <= 31; startDay++)
        {
            for (var day = new DateTime(2026, 1, 1); day < new DateTime(2028, 1, 1); day = day.AddDays(3))
            {
                var w = QuotaPeriod.Monthly(day, startDay);
                var today = DateOnly.FromDateTime(day);
                Assert.InRange(today, w.From, w.To);
                Assert.InRange(w.To.DayNumber - w.From.DayNumber + 1, 28, 31);
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public void Monthly_InvalidStartDay_Throws(int startDay)
        => Assert.Throws<ArgumentOutOfRangeException>(() => QuotaPeriod.Monthly(new DateTime(2026, 11, 12), startDay));
}
