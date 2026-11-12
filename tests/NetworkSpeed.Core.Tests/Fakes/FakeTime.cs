namespace NetworkSpeed.Core.Tests.Fakes;

/// <summary>TimeProvider yang waktunya diatur test; zona waktu UTC agar deterministik.</summary>
internal sealed class FakeTime(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}
