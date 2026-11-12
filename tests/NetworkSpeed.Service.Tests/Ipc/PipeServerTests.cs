using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using NetworkSpeed.Core.Ipc;
using NetworkSpeed.Service.Ipc;

namespace NetworkSpeed.Service.Tests.Ipc;

public sealed class PipeServerTests : IAsyncDisposable
{
    private readonly string _pipeName = "NetworkSpeed.Test." + Guid.NewGuid().ToString("N");
    private readonly CancellationTokenSource _cts = new();
    private readonly RecordingHandler _handler = new();
    private Task? _server;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        if (_server is not null)
        {
            await _server.WaitAsync(TimeSpan.FromSeconds(5));
        }

        _cts.Dispose();
    }

    private ServiceClient Client(TimeSpan? timeout = null) => new(_pipeName, timeout ?? TimeSpan.FromSeconds(3), requireSessionZeroServer: false);

    private void StartServer(TimeSpan? requestTimeout = null, int maxInstances = 16)
    {
        var server = new PipeServer(_pipeName, _handler, new TestLog(), maxInstances, requestTimeout ?? TimeSpan.FromSeconds(1));
        _server = server.RunAsync(_cts.Token);
    }

    [Fact]
    public async Task Client_GetStatus_RoundTrip()
    {
        StartServer();
        var response = await Client().SendAsync(new PipeRequest(PipeProtocol.Version, PipeOperations.GetStatus), Ct);
        Assert.True(response.Ok);
        Assert.Equal("test", response.Status!.Version);
    }

    [Fact]
    public async Task Server_PassesCallerSessionId()
    {
        StartServer();
        await Client().SendAsync(new PipeRequest(PipeProtocol.Version, PipeOperations.GetStatus), Ct);
        Assert.Equal(Process.GetCurrentProcess().SessionId, _handler.LastSessionId);
    }

    [Fact]
    public async Task Server_UnsupportedVersion_ErrorCode()
    {
        StartServer();
        var response = await Client().SendAsync(new PipeRequest(99, PipeOperations.GetStatus), Ct);
        Assert.Equal(PipeErrors.UnsupportedVersion, response.ErrorCode);
        Assert.Equal(0, _handler.Calls);
    }

    [Fact]
    public async Task Server_HandlerThrows_InternalErrorWithoutDetails()
    {
        _handler.Throw = true;
        StartServer();
        var response = await Client().SendAsync(new PipeRequest(PipeProtocol.Version, PipeOperations.GetStatus), Ct);
        Assert.False(response.Ok);
        Assert.Equal(PipeErrors.Internal, response.ErrorCode);
        Assert.Null(response.Status);
    }

    [Fact]
    public async Task Server_OversizedRequest_Rejected()
    {
        StartServer();
        using (var raw = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await raw.ConnectAsync(3000, Ct);
            await raw.WriteAsync(BitConverter.GetBytes(1_000_000), Ct);
            await raw.FlushAsync(Ct);
            var buffer = new byte[4];
            var read = await raw.ReadAsync(buffer, Ct);
            Assert.True(read == 0 || BitConverter.ToInt32(buffer) > 0);
        }

        Assert.True((await Client().SendAsync(new PipeRequest(PipeProtocol.Version, PipeOperations.GetStatus), Ct)).Ok);
    }

    [Fact]
    public async Task Server_SilentClient_TimesOut()
    {
        StartServer(requestTimeout: TimeSpan.FromMilliseconds(500));
        using var raw = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await raw.ConnectAsync(3000, Ct);
        var sw = Stopwatch.StartNew();
        var read = await raw.ReadAsync(new byte[1], Ct).AsTask().WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.Equal(0, read);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(4));
    }

    [Fact]
    public async Task Server_ManyClients_OthersStillServed()
    {
        StartServer(requestTimeout: TimeSpan.FromMilliseconds(500), maxInstances: 4);
        var silent = new List<NamedPipeClientStream>();
        try
        {
            for (var i = 0; i < 4; i++)
            {
                var c = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await c.ConnectAsync(3000, Ct);
                silent.Add(c);
            }

            var response = await Client(TimeSpan.FromSeconds(5)).SendAsync(new PipeRequest(PipeProtocol.Version, PipeOperations.GetStatus), Ct);
            Assert.True(response.Ok);
        }
        finally
        {
            silent.ForEach(c => c.Dispose());
        }
    }

    [Fact]
    public async Task Server_ClientsThatNeverReadOrDisconnectEarly_DoNotKillListeners()
    {
        _handler.LargeResponse = true;
        StartServer(requestTimeout: TimeSpan.FromMilliseconds(300), maxInstances: 2);
        var request = PipeProtocol.Serialize(new PipeRequest(PipeProtocol.Version, PipeOperations.GetStatus));
        for (var i = 0; i < 6; i++)
        {
            // Klien mengirim request valid lalu tidak pernah membaca jawaban besar (write server tertahan sampai timeout).
            using var reader = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await reader.ConnectAsync(3000, Ct);
            await PipeProtocol.WriteAsync(reader, request, Ct);
            await Task.Delay(600, Ct);
        }

        for (var i = 0; i < 4; i++)
        {
            // Klien connect lalu langsung putus.
            using var quitter = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await quitter.ConnectAsync(3000, Ct);
        }

        _handler.LargeResponse = false;
        var response = await Client(TimeSpan.FromSeconds(5)).SendAsync(new PipeRequest(PipeProtocol.Version, PipeOperations.GetStatus), Ct);
        Assert.True(response.Ok);
        Assert.False(_server!.IsFaulted);
    }

    [Fact]
    public async Task Client_NoServer_ReturnsUnavailable()
    {
        var response = await Client(TimeSpan.FromMilliseconds(300)).SendAsync(new PipeRequest(PipeProtocol.Version, PipeOperations.GetStatus), Ct);
        Assert.False(response.Ok);
        Assert.Equal(PipeErrors.Unavailable, response.ErrorCode);
    }

    [Fact]
    public async Task Client_RequireSessionZero_RejectsUserSessionServer()
    {
        StartServer();
        var strict = new ServiceClient(_pipeName, TimeSpan.FromSeconds(3), requireSessionZeroServer: true);
        var response = await strict.SendAsync(new PipeRequest(PipeProtocol.Version, PipeOperations.GetStatus), Ct);
        Assert.Equal(PipeErrors.Unavailable, response.ErrorCode);
        Assert.Equal(0, _handler.Calls);
    }

    [Fact]
    public void Server_Acl_DeniesNetwork_AllowsInteractive()
    {
        var rules = PipeServer.CreateSecurity().GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToList();
        Assert.Contains(rules, r => r.IdentityReference.Value == "S-1-5-2" && r.AccessControlType == AccessControlType.Deny);
        Assert.Contains(rules, r => r.IdentityReference.Value == "S-1-5-4" && r.AccessControlType == AccessControlType.Allow);
        Assert.DoesNotContain(rules, r => r.IdentityReference.Value == "S-1-1-0");
    }

    private sealed class RecordingHandler : IPipeRequestHandler
    {
        public int Calls { get; private set; }

        public int LastSessionId { get; private set; } = -1;

        public bool Throw { get; set; }

        public bool LargeResponse { get; set; }

        public PipeResponse Handle(PipeRequest request, int callerSessionId)
        {
            Calls++;
            LastSessionId = callerSessionId;
            if (Throw)
            {
                throw new InvalidOperationException(@"secret C:\path");
            }

            if (LargeResponse)
            {
                var points = Enumerable.Range(0, 20_000).Select(i => new Core.History.HistoryPoint($"2026-11-12T{i:D5}", 1, 1)).ToList();
                return PipeResponse.Success() with { History = points };
            }

            return PipeResponse.Success() with { Status = new ServiceStatus("test", 1, true) };
        }
    }
}
