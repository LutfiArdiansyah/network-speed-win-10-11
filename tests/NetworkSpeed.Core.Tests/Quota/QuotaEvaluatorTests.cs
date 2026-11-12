using NetworkSpeed.Core.Quota;

namespace NetworkSpeed.Core.Tests.Quota;

public class QuotaEvaluatorTests
{
    private const ulong Gb = 1024UL * 1024 * 1024;
    private static readonly QuotaWindow Today = QuotaPeriod.Daily(new DateTime(2026, 11, 12, 10, 0, 0));

    [Fact]
    public void Evaluate_Below80_None()
        => Assert.Empty(QuotaEvaluator.Evaluate(Today, 3 * Gb, 5 * Gb, new HashSet<string>()));

    [Fact]
    public void Evaluate_Cross80_Once()
    {
        var first = Assert.Single(QuotaEvaluator.Evaluate(Today, 4 * Gb, 5 * Gb, new HashSet<string>()));
        Assert.Equal((80, true), (first.Percent, first.IsShown));
        var sent = new HashSet<string> { first.StateKey };
        Assert.Empty(QuotaEvaluator.Evaluate(Today, 4 * Gb, 5 * Gb, sent));
    }

    [Fact]
    public void Evaluate_JumpTo120_Only100Shown_80MarkedToo()
    {
        var alerts = QuotaEvaluator.Evaluate(Today, 6 * Gb, 5 * Gb, new HashSet<string>());
        Assert.Equal(2, alerts.Count);
        Assert.Equal([100], alerts.Where(a => a.IsShown).Select(a => a.Percent));
        Assert.Contains(alerts, a => a.Percent == 80 && !a.IsShown);
    }

    [Fact]
    public void Evaluate_LimitChanged_Rearms()
    {
        var first = QuotaEvaluator.Evaluate(Today, 4 * Gb, 5 * Gb, new HashSet<string>());
        var sent = first.Select(a => a.StateKey).ToHashSet();
        var afterChange = QuotaEvaluator.Evaluate(Today, 4 * Gb, 4500UL * 1024 * 1024, sent);
        Assert.Equal([80], afterChange.Where(a => a.IsShown).Select(a => a.Percent));
    }

    [Fact]
    public void Evaluate_ZeroLimit_None()
        => Assert.Empty(QuotaEvaluator.Evaluate(Today, 10 * Gb, 0, new HashSet<string>()));

    [Fact]
    public void Evaluate_StateKey_ContainsPeriodLimitAndPercent()
    {
        var alert = Assert.Single(QuotaEvaluator.Evaluate(Today, 4 * Gb, 5 * Gb, new HashSet<string>()));
        Assert.Equal($"daily:2026-11-12:{5 * Gb}:80", alert.StateKey);
    }
}
