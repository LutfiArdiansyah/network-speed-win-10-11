using System.Diagnostics;

namespace NetworkSpeed.Core.Time;

/// <summary>Implementasi <see cref="IMonotonicClock"/> berbasis <see cref="Stopwatch"/>.</summary>
public sealed class StopwatchClock : IMonotonicClock
{
    public TimeSpan Elapsed => Stopwatch.GetElapsedTime(0);
}
