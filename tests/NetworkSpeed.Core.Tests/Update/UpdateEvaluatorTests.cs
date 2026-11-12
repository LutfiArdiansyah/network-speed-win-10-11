using NetworkSpeed.Core.Update;

namespace NetworkSpeed.Core.Tests.Update;

public class UpdateEvaluatorTests
{
    private static UpdateManifest Manifest(Version version) => new(version, "setup.exe", new string('a', 64), string.Empty);

    [Theory]
    [InlineData("1.0.1")]
    [InlineData("1.1.0")]
    [InlineData("2.0.0")]
    [InlineData("1.0.0.1")]
    public void Evaluate_Newer_Available(string latest) =>
        Assert.Equal(UpdateState.Available, UpdateEvaluator.Evaluate(new Version(1, 0, 0), Manifest(Version.Parse(latest))));

    [Theory]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("1.0.0.0", "1.0.0")]
    [InlineData("1.0.0", "1.0.0.0")]
    [InlineData("1.2.0", "1.1.9")]
    public void Evaluate_SameOrOlder_UpToDate(string current, string latest) =>
        Assert.Equal(UpdateState.UpToDate, UpdateEvaluator.Evaluate(Version.Parse(current), Manifest(Version.Parse(latest))));
}
