using System.Text;
using NetworkSpeed.Core.Ipc;

namespace NetworkSpeed.Core.Tests.Ipc;

public class PipeProtocolTests
{
    private static readonly Guid A = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static PipeRequest History(string from, string to, HistoryResolution resolution, Guid[]? adapters = null) =>
        new(PipeProtocol.Version, PipeOperations.GetHistory) { AdapterIds = adapters ?? [A], From = from, To = to, Resolution = resolution };

    [Fact]
    public void Validate_ValidRequests_ReturnNull()
    {
        Assert.Null(PipeRequestValidator.Validate(new PipeRequest(PipeProtocol.Version, PipeOperations.GetStatus)));
        Assert.Null(PipeRequestValidator.Validate(new PipeRequest(PipeProtocol.Version, PipeOperations.GetAdapters)));
        Assert.Null(PipeRequestValidator.Validate(History("2026-11-05T10:15", "2026-11-12T10:14", HistoryResolution.Minute)));
        Assert.Null(PipeRequestValidator.Validate(History("2026-09-12", "2026-11-12", HistoryResolution.Day)));
        Assert.Null(PipeRequestValidator.Validate(new PipeRequest(PipeProtocol.Version, PipeOperations.GetPerApp) { Period = PerAppPeriod.Today }));
    }

    [Fact]
    public void Validate_WrongVersion_Unsupported()
        => Assert.Equal(PipeErrors.UnsupportedVersion, PipeRequestValidator.Validate(new PipeRequest(2, PipeOperations.GetStatus)));

    [Theory]
    [InlineData("DeleteEverything")]
    [InlineData("")]
    public void Validate_UnknownOperation_Invalid(string op)
        => Assert.Equal(PipeErrors.InvalidRequest, PipeRequestValidator.Validate(new PipeRequest(PipeProtocol.Version, op)));

    [Theory]
    [InlineData("2026-11-12T10:00", "2026-11-12T09:00")]  // From > To
    [InlineData("2026-11-12 10:00", "2026-11-12T11:00")]  // format salah
    [InlineData("2026-11-12", "2026-11-12")]              // format hari untuk resolusi menit
    [InlineData("2026-11-01T00:00", "2026-11-12T00:00")]  // > 10.080 titik
    public void Validate_BadMinuteRange_Invalid(string from, string to)
        => Assert.Equal(PipeErrors.InvalidRequest, PipeRequestValidator.Validate(History(from, to, HistoryResolution.Minute)));

    [Fact]
    public void Validate_DayRangeOver62_Invalid()
        => Assert.Equal(PipeErrors.InvalidRequest, PipeRequestValidator.Validate(History("2026-08-01", "2026-11-12", HistoryResolution.Day)));

    [Fact]
    public void Validate_AdapterCount_Invalid()
    {
        Assert.Equal(PipeErrors.InvalidRequest, PipeRequestValidator.Validate(History("2026-11-12T10:00", "2026-11-12T11:00", HistoryResolution.Minute, [])));
        var tooMany = Enumerable.Range(0, 65).Select(_ => Guid.NewGuid()).ToArray();
        Assert.Equal(PipeErrors.InvalidRequest, PipeRequestValidator.Validate(History("2026-11-12T10:00", "2026-11-12T11:00", HistoryResolution.Minute, tooMany)));
    }

    [Fact]
    public void Validate_PerAppWithoutPeriod_Invalid()
        => Assert.Equal(PipeErrors.InvalidRequest, PipeRequestValidator.Validate(new PipeRequest(PipeProtocol.Version, PipeOperations.GetPerApp)));

    [Fact]
    public async Task Framing_RoundTrip()
    {
        using var stream = new MemoryStream();
        var request = History("2026-11-12T10:00", "2026-11-12T11:00", HistoryResolution.Minute);
        await PipeProtocol.WriteAsync(stream, PipeProtocol.Serialize(request), TestContext.Current.CancellationToken);
        stream.Position = 0;
        var payload = await PipeProtocol.ReadAsync(stream, PipeProtocol.MaxRequestBytes, TestContext.Current.CancellationToken);
        // Record berisi array memakai reference equality; bandingkan hasil serialisasinya.
        Assert.Equal(PipeProtocol.Serialize(request), PipeProtocol.Serialize(PipeProtocol.DeserializeRequest(payload)!));
    }

    [Fact]
    public async Task Framing_LengthOverMax_Rejected()
    {
        using var stream = new MemoryStream();
        stream.Write(BitConverter.GetBytes(1_000_000));
        stream.Position = 0;
        await Assert.ThrowsAsync<PipeProtocolException>(() => PipeProtocol.ReadAsync(stream, PipeProtocol.MaxRequestBytes, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Framing_TruncatedPayload_Rejected()
    {
        using var stream = new MemoryStream();
        stream.Write(BitConverter.GetBytes(100));
        stream.Write("{}"u8);
        stream.Position = 0;
        await Assert.ThrowsAsync<PipeProtocolException>(() => PipeProtocol.ReadAsync(stream, PipeProtocol.MaxRequestBytes, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Deserialize_GarbageJson_ReturnsNull()
        => Assert.Null(PipeProtocol.DeserializeRequest(Encoding.UTF8.GetBytes("not json {")));

    [Fact]
    public void Response_RoundTrip()
    {
        var response = PipeResponse.Error(PipeErrors.Internal);
        Assert.Equal(response, PipeProtocol.DeserializeResponse(PipeProtocol.Serialize(response)));
    }
}
