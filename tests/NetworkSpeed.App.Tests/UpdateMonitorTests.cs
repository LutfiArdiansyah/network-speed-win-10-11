using System.IO;
using System.Text;
using NetworkSpeed.App.Features;
using NetworkSpeed.Core.Logging;
using NetworkSpeed.Core.Storage;

namespace NetworkSpeed.App.Tests;

public sealed class UpdateMonitorTests : IDisposable
{
    private const string Folder = @"\\server\share\NetworkSpeed";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ns-upd-" + Guid.NewGuid().ToString("N"));
    private readonly List<(string Title, string Text)> _balloons = [];
    private readonly FakeLog _log = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public async Task Check_FolderNotConfigured_NotConfigured_NoBalloon()
    {
        var reader = new FakeReader(_ => Task.FromResult<byte[]?>(Manifest("2.0.0")));
        using var monitor = Create(reader, folder: null);
        Assert.Equal(UpdateCheckResult.NotConfigured, await monitor.CheckAsync(Ct));
        Assert.Equal(0, reader.Calls);
        Assert.Empty(_balloons);
    }

    [Fact]
    public async Task Check_Disabled_NoRead()
    {
        var reader = new FakeReader(_ => Task.FromResult<byte[]?>(Manifest("2.0.0")));
        using var monitor = Create(reader, enabled: false);
        Assert.Equal(UpdateCheckResult.Disabled, await monitor.CheckAsync(Ct));
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public async Task Check_ReaderHangs_TimesOutAsUnreachable()
    {
        var reader = new FakeReader(_ => new TaskCompletionSource<byte[]?>().Task);
        using var monitor = Create(reader, timeout: TimeSpan.FromMilliseconds(100));
        Assert.Equal(UpdateCheckResult.Unreachable, await monitor.CheckAsync(Ct));
        Assert.Equal("Update folder not reachable", monitor.StatusText);
    }

    [Fact]
    public async Task Check_ReaderThrowsIOException_Unreachable_LogHasNoPath()
    {
        var reader = new FakeReader(_ => throw new IOException(@"The network path \\server\share was not found"));
        using var monitor = Create(reader);
        Assert.Equal(UpdateCheckResult.Unreachable, await monitor.CheckAsync(Ct));
        Assert.DoesNotContain(_log.Lines, l => l.Contains("server", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Check_InvalidManifest_Invalid_NoBalloon()
    {
        var reader = new FakeReader(_ => Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes("{\"version\":\"v2\"}")));
        using var monitor = Create(reader);
        Assert.Equal(UpdateCheckResult.Invalid, await monitor.CheckAsync(Ct));
        Assert.Empty(_balloons);
    }

    [Fact]
    public async Task Check_NewerVersion_BalloonOnce_ThenNotAgain()
    {
        var reader = new FakeReader(_ => Task.FromResult<byte[]?>(Manifest("1.1.0")));
        using (var monitor = Create(reader))
        {
            Assert.Equal(UpdateCheckResult.Available, await monitor.CheckAsync(Ct));
            Assert.Equal(UpdateCheckResult.Available, await monitor.CheckAsync(Ct));
            Assert.Equal("Version 1.1.0 is available. Your IT team will install it.", monitor.StatusText);
        }

        // App restart: state.json dibaca ulang dari disk.
        using (var restarted = Create(reader))
        {
            await restarted.CheckAsync(Ct);
        }

        var balloon = Assert.Single(_balloons);
        Assert.Equal("A new version (1.1.0) is available. Your IT team will install it.", balloon.Text);
        Assert.Contains("update:1.1.0", File.ReadAllText(Path.Combine(_dir, "state.json")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_SameVersion_UpToDate()
    {
        var reader = new FakeReader(_ => Task.FromResult<byte[]?>(Manifest("1.0.0")));
        using var monitor = Create(reader);
        Assert.Equal(UpdateCheckResult.UpToDate, await monitor.CheckAsync(Ct));
        Assert.Equal("Up to date (1.0.0)", monitor.StatusText);
        Assert.Empty(_balloons);
    }

    private static byte[] Manifest(string version) => Encoding.UTF8.GetBytes(
        "{\"version\":\"" + version + "\",\"installer\":\"NetworkSpeedSetup-" + version + ".exe\",\"sha256\":\"" + new string('a', 64) + "\"}");

    private UpdateMonitor Create(FakeReader reader, string? folder = Folder, bool enabled = true, TimeSpan? timeout = null) =>
        new(
            reader,
            () => folder,
            () => enabled,
            new Version(1, 0, 0),
            new NotificationStateStore(Path.Combine(_dir, "state.json"), TimeProvider.System),
            (title, text) => _balloons.Add((title, text)),
            _log,
            timeout);

    private sealed class FakeReader(Func<string, Task<byte[]?>> read) : IManifestReader
    {
        public int Calls { get; private set; }

        public Task<byte[]?> ReadAsync(string folder, CancellationToken cancellationToken)
        {
            Calls++;
            return read(folder);
        }
    }

    private sealed class FakeLog : IAppLog
    {
        public List<string> Lines { get; } = [];

        public void Info(string message) => Lines.Add(message);

        public void Warn(string message) => Lines.Add(message);

        public void Error(string message, Exception? exception = null) => Lines.Add(message + " " + exception?.Message);
    }
}
