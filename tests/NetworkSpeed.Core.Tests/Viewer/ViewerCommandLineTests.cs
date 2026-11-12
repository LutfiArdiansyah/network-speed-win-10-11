using NetworkSpeed.Core.Viewer;

namespace NetworkSpeed.Core.Tests.Viewer;

public class ViewerCommandLineTests
{
    [Fact]
    public void BuildThenParse_History_RoundTrips()
    {
        var args = new ViewerArguments(ViewerKind.History, 4242, [(1.5, 2048.25), (0, 1e9)]);
        var parsed = ViewerCommandLine.Parse(ViewerCommandLine.Build(args));
        Assert.NotNull(parsed);
        Assert.Equal(ViewerKind.History, parsed.Kind);
        Assert.Equal(4242, parsed.ParentProcessId);
        Assert.Equal(args.Live, parsed.Live);
    }

    [Fact]
    public void BuildThenParse_PerAppWithoutLive_RoundTrips()
    {
        var parsed = ViewerCommandLine.Parse(ViewerCommandLine.Build(new ViewerArguments(ViewerKind.PerApp, 7, [])));
        Assert.NotNull(parsed);
        Assert.Equal(ViewerKind.PerApp, parsed.Kind);
        Assert.Empty(parsed.Live);
    }

    [Theory]
    [InlineData("--viewer", "settings", "--parent", "10")]
    [InlineData("--viewer", "history")]
    [InlineData("--viewer", "history", "--parent", "0")]
    [InlineData("--viewer", "history", "--parent", "-5")]
    [InlineData("--viewer", "history", "--parent", "abc")]
    [InlineData("--viewer")]
    public void Parse_InvalidKindOrParent_ReturnsNull(params string[] args) =>
        Assert.Null(ViewerCommandLine.Parse(args));

    [Theory]
    [InlineData("1:2;garbage")]
    [InlineData("NaN:1")]
    [InlineData("-1:5")]
    [InlineData("Infinity:0")]
    [InlineData("1;2")]
    public void Parse_MalformedLive_LiveEmptyButViewerStillOpens(string live)
    {
        var parsed = ViewerCommandLine.Parse(["--viewer", "history", "--parent", "10", "--live", live]);
        Assert.NotNull(parsed);
        Assert.Empty(parsed.Live);
    }

    [Fact]
    public void Parse_MoreThanSixtySamples_KeepsLastSixty()
    {
        var samples = Enumerable.Range(0, 75).Select(i => ((double)i, (double)i)).ToList();
        var parsed = ViewerCommandLine.Parse(ViewerCommandLine.Build(new ViewerArguments(ViewerKind.History, 10, samples)));
        Assert.NotNull(parsed);
        Assert.Equal(ViewerCommandLine.MaxLiveSamples, parsed.Live.Count);
        Assert.Equal((74d, 74d), parsed.Live[^1]);
        Assert.Equal((15d, 15d), parsed.Live[0]);
    }

    [Fact]
    public void Parse_OversizedLiveArgument_LiveEmpty()
    {
        var huge = string.Join(';', Enumerable.Repeat("1:1", 5000));
        var parsed = ViewerCommandLine.Parse(["--viewer", "history", "--parent", "10", "--live", huge]);
        Assert.NotNull(parsed);
        Assert.Empty(parsed.Live);
    }

    [Theory]
    [InlineData]
    [InlineData("--autostart")]
    [InlineData("history", "--viewer")]
    public void IsViewer_NormalStart_False(params string[] args) =>
        Assert.False(ViewerCommandLine.IsViewer(args));

    [Fact]
    public void IsViewer_ViewerFlagFirst_True() =>
        Assert.True(ViewerCommandLine.IsViewer(["--viewer", "perapp", "--parent", "1"]));
}
