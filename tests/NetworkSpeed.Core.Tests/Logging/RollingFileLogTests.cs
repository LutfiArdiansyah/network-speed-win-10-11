using NetworkSpeed.Core.Logging;

namespace NetworkSpeed.Core.Tests.Logging;

public sealed class RollingFileLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ns-log-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void RollingFileLog_ExceedsMax_RotatesAndKeepsFiveFiles()
    {
        var log = new RollingFileLog(_dir, maxBytes: 200, maxFiles: 5);
        for (var i = 0; i < 100; i++)
        {
            log.Info($"line {i:D3} some padding text");
        }

        var files = Directory.GetFiles(_dir, "*.log");
        Assert.Equal(5, files.Length);
        Assert.All(files, f => Assert.True(new FileInfo(f).Length <= 200 + 100));
        Assert.Contains("line 099", File.ReadAllText(Path.Combine(_dir, "app.log")), StringComparison.Ordinal);
    }

    [Fact]
    public void RollingFileLog_Error_DoesNotWriteExceptionMessage()
    {
        var log = new RollingFileLog(_dir);
        log.Error("load failed", new InvalidOperationException(@"C:\secret\path 10.0.0.5"));
        var text = File.ReadAllText(Path.Combine(_dir, "app.log"));
        Assert.Contains("load failed", text, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RollingFileLog_BaseName_WritesAndRotatesUnderThatName()
    {
        var log = new RollingFileLog(_dir, maxBytes: 200, maxFiles: 3, baseName: "viewer-history");
        for (var i = 0; i < 20; i++)
        {
            log.Info($"line {i:D3} some padding text");
        }

        Assert.True(File.Exists(Path.Combine(_dir, "viewer-history.log")));
        Assert.True(File.Exists(Path.Combine(_dir, "viewer-history.1.log")));
        Assert.False(File.Exists(Path.Combine(_dir, "app.log")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("..\\app")]
    [InlineData("a/b")]
    [InlineData("app.log")]
    public void RollingFileLog_InvalidBaseName_Throws(string baseName) =>
        Assert.ThrowsAny<ArgumentException>(() => new RollingFileLog(_dir, baseName: baseName));

    [Fact]
    public void RollingFileLog_NewlinesInMessage_AreEscaped()
    {
        var log = new RollingFileLog(_dir);
        log.Warn("first\r\nFAKE ERROR injected");
        var lines = File.ReadAllLines(Path.Combine(_dir, "app.log"));
        Assert.Single(lines);
        Assert.Contains("WARN", lines[0], StringComparison.Ordinal);
    }
}
