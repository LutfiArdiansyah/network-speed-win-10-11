using NetworkSpeed.Core.Adapters;
using NetworkSpeed.Core.Sampling;
using NetworkSpeed.Core.Tests.Fakes;

namespace NetworkSpeed.Core.Tests.Sampling;

public class SpeedSamplerTests
{
    private static readonly Guid Wifi = new(1, 0, 0, new byte[8]);
    private static readonly Guid Lan = new(2, 0, 0, new byte[8]);
    private static readonly AdapterSelection Auto = new(AdapterSelectionMode.Auto, null, false);

    private static (SpeedSampler Sampler, FakeCounterSource Source, FakeClock Clock) Create()
    {
        var source = new FakeCounterSource();
        source.Adapters.Add(new AdapterInfo(Wifi, "Wi-Fi", AdapterKind.Wireless, true, true));
        source.Adapters.Add(new AdapterInfo(Lan, "Ethernet", AdapterKind.Ethernet, true, false));
        source.Set(Wifi, 0, 0);
        source.Set(Lan, 0, 0);
        var clock = new FakeClock();
        return (new SpeedSampler(source, clock, TimeSpan.FromSeconds(10)), source, clock);
    }

    [Fact]
    public void Sample_First_ReturnsNullBaseline()
    {
        var (sampler, _, _) = Create();
        Assert.Null(sampler.Sample(Auto));
    }

    [Fact]
    public void Sample_Second_SumsSelectedAdaptersOnly()
    {
        var (sampler, source, clock) = Create();
        sampler.Sample(Auto);
        clock.Advance(1);
        source.Set(Wifi, 2_000, 1_000);
        source.Set(Lan, 999_999, 999_999);
        var reading = sampler.Sample(Auto)!;
        Assert.Equal(2_000d, reading.DownBytesPerSecond);
        Assert.Equal(1_000d, reading.UpBytesPerSecond);
        Assert.False(reading.IsOffline);
        Assert.Equal([Wifi], reading.SelectedAdapters.Select(a => a.Id));
    }

    [Fact]
    public void Sample_NoSelectedAdapter_ReturnsOffline()
    {
        var (sampler, source, _) = Create();
        source.Adapters.Clear();
        var reading = sampler.Sample(Auto);
        Assert.NotNull(reading);
        Assert.True(reading!.IsOffline);
        Assert.Equal(0d, reading.UpBytesPerSecond);
        Assert.Equal(0d, reading.DownBytesPerSecond);
    }

    [Fact]
    public void Sample_AdaptersCachedUntilRefreshInterval()
    {
        var (sampler, source, clock) = Create();
        for (var i = 0; i < 5; i++)
        {
            sampler.Sample(Auto);
            clock.Advance(2);
        }

        Assert.Equal(1, source.ReadAdaptersCalls);
        clock.Advance(2);
        sampler.Sample(Auto);
        Assert.Equal(2, source.ReadAdaptersCalls);
    }

    [Fact]
    public void InvalidateAdapters_ForcesReadOnNextSample()
    {
        var (sampler, source, clock) = Create();
        sampler.Sample(Auto);
        clock.Advance(1);
        sampler.InvalidateAdapters();
        sampler.Sample(Auto);
        Assert.Equal(2, source.ReadAdaptersCalls);
    }

    [Fact]
    public void OnResume_NextSampleIsBaseline()
    {
        var (sampler, source, clock) = Create();
        sampler.Sample(Auto);
        clock.Advance(1);
        sampler.Sample(Auto);
        sampler.OnResume();
        clock.Advance(1);
        source.Set(Wifi, 50_000_000, 0);
        Assert.Null(sampler.Sample(Auto));
    }

    [Fact]
    public void Sample_SessionTotals_AccumulateAcrossCounterReset()
    {
        var (sampler, source, clock) = Create();
        sampler.Sample(Auto);
        clock.Advance(1);
        source.Set(Wifi, 5_000, 3_000);
        var first = sampler.Sample(Auto)!;
        Assert.Equal(5_000UL, first.SessionReceivedBytes);
        Assert.Equal(3_000UL, first.SessionSentBytes);

        clock.Advance(1);
        source.Set(Wifi, 100, 50);
        var afterReset = sampler.Sample(Auto)!;
        Assert.Equal(5_000UL, afterReset.SessionReceivedBytes);

        clock.Advance(1);
        source.Set(Wifi, 1_100, 50);
        Assert.Equal(6_000UL, sampler.Sample(Auto)!.SessionReceivedBytes);
    }
}
