using NetworkSpeed.Core.Time;

namespace NetworkSpeed.Core.Tests.Fakes;

internal sealed class FakeClock : IMonotonicClock
{
    public TimeSpan Elapsed { get; set; }

    public void Advance(double seconds) => Elapsed += TimeSpan.FromSeconds(seconds);
}
