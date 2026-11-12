using System.IO;
using NetworkSpeed.App.Features;
using NetworkSpeed.Core.Adapters;
using NetworkSpeed.Core.Ipc;

namespace NetworkSpeed.App.Tests;

public class ServiceConnectionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Refresh_ServiceUp_Available()
    {
        var transport = new FakeTransport { Up = true };
        using var connection = new ServiceConnection(transport, new ReconnectBackoff());
        var next = await connection.RefreshAsync(Ct);
        Assert.True(connection.IsAvailable);
        Assert.True(connection.IsPerAppAvailable);
        Assert.Single(connection.Adapters);
        Assert.Equal(ServiceConnection.HealthyPollInterval, next);
    }

    [Fact]
    public async Task Refresh_ServiceDown_ThenUp_Recovers()
    {
        var transport = new FakeTransport { Up = false };
        using var connection = new ServiceConnection(transport, new ReconnectBackoff());
        var changes = 0;
        connection.StateChanged += () => changes++;

        Assert.Equal(TimeSpan.FromSeconds(1), await connection.RefreshAsync(Ct));
        Assert.Equal(TimeSpan.FromSeconds(2), await connection.RefreshAsync(Ct));
        Assert.False(connection.IsAvailable);
        Assert.Empty(connection.Adapters);

        transport.Up = true;
        Assert.Equal(ServiceConnection.HealthyPollInterval, await connection.RefreshAsync(Ct));
        Assert.True(connection.IsAvailable);
        Assert.Equal(1, changes);

        transport.Up = false;
        Assert.Equal(TimeSpan.FromSeconds(1), await connection.RefreshAsync(Ct));
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task Query_Unavailable_FlipsState()
    {
        var transport = new FakeTransport { Up = true };
        using var connection = new ServiceConnection(transport, new ReconnectBackoff());
        await connection.RefreshAsync(Ct);
        transport.Up = false;
        var response = await connection.QueryAsync(new PipeRequest(PipeProtocol.Version, PipeOperations.GetStatus), Ct);
        Assert.Equal(PipeErrors.Unavailable, response.ErrorCode);
        Assert.False(connection.IsAvailable);
    }

    [Fact]
    public async Task Refresh_EtwOff_PerAppUnavailable()
    {
        var transport = new FakeTransport { Up = true, Etw = false };
        using var connection = new ServiceConnection(transport, new ReconnectBackoff());
        await connection.RefreshAsync(Ct);
        Assert.True(connection.IsAvailable);
        Assert.False(connection.IsPerAppAvailable);
    }

    [Fact]
    public async Task Refresh_TransportThrows_TreatedAsUnavailable()
    {
        var transport = new FakeTransport { Up = true, Throw = true };
        using var connection = new ServiceConnection(transport, new ReconnectBackoff());
        await connection.RefreshAsync(Ct);
        Assert.False(connection.IsAvailable);
    }

    [Fact]
    public async Task Refresh_ThrowingSubscriber_DoesNotBreakConnection()
    {
        var transport = new FakeTransport { Up = true };
        using var connection = new ServiceConnection(transport, new ReconnectBackoff());
        connection.StateChanged += () => throw new InvalidOperationException("subscriber bug");
        Assert.Equal(ServiceConnection.HealthyPollInterval, await connection.RefreshAsync(Ct));
        Assert.True(connection.IsAvailable);
    }

    [Fact]
    public async Task FirstCheck_CompletesAfterFirstRefresh_EvenWhenServiceDown()
    {
        var transport = new FakeTransport { Up = false };
        using var connection = new ServiceConnection(transport, new ReconnectBackoff());
        Assert.False(connection.FirstCheck.IsCompleted);
        await connection.RefreshAsync(Ct);
        Assert.True(connection.FirstCheck.IsCompletedSuccessfully);
        Assert.False(connection.IsAvailable);
    }

    [Fact]
    public async Task FirstCheck_TransportThrows_StillCompletes()
    {
        var transport = new FakeTransport { Up = true, Throw = true };
        using var connection = new ServiceConnection(transport, new ReconnectBackoff());
        await connection.RefreshAsync(Ct);
        Assert.True(connection.FirstCheck.IsCompletedSuccessfully);
    }

    private sealed class FakeTransport : IServiceTransport
    {
        public bool Up { get; set; }

        public bool Etw { get; set; } = true;

        public bool Throw { get; set; }

        public Task<PipeResponse> SendAsync(PipeRequest request, CancellationToken cancellationToken)
        {
            if (Throw)
            {
                throw new IOException("pipe broken");
            }

            if (!Up)
            {
                return Task.FromResult(PipeResponse.Error(PipeErrors.Unavailable));
            }

            var response = request.Operation == PipeOperations.GetAdapters
                ? PipeResponse.Success() with { Adapters = [new AdapterInfo(Guid.NewGuid(), "Wi-Fi", AdapterKind.Wireless, true, true)] }
                : PipeResponse.Success() with { Status = new ServiceStatus("1", 10, Etw) };
            return Task.FromResult(response);
        }
    }
}
