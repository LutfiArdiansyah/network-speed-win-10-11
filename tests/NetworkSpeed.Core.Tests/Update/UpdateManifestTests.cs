using System.Text;
using NetworkSpeed.Core.Update;

namespace NetworkSpeed.Core.Tests.Update;

public class UpdateManifestTests
{
    private const string Hash = "ABCDEF0123456789abcdef0123456789ABCDEF0123456789abcdef0123456789";

    private static UpdateManifest? Parse(string json) => UpdateManifest.TryParse(Encoding.UTF8.GetBytes(json));

    private static string Json(string version = "1.2.3", string installer = "NetworkSpeedSetup-1.2.3.exe", string sha256 = Hash, string? notes = "Bug fixes") =>
        "{\"version\":\"" + version + "\",\"installer\":\"" + installer.Replace("\\", "\\\\", StringComparison.Ordinal) + "\",\"sha256\":\"" + sha256 + "\""
        + (notes is null ? string.Empty : ",\"notes\":\"" + notes + "\"") + "}";

    [Fact]
    public void TryParse_Valid_ReturnsManifest()
    {
        var manifest = Parse(Json());
        Assert.NotNull(manifest);
        Assert.Equal(new Version(1, 2, 3), manifest.Version);
        Assert.Equal("NetworkSpeedSetup-1.2.3.exe", manifest.InstallerFileName);
        Assert.Equal(Hash.ToLowerInvariant(), manifest.Sha256);
        Assert.Equal("Bug fixes", manifest.Notes);
    }

    [Fact]
    public void TryParse_FourPartVersionAndNoNotes_Accepted()
    {
        var manifest = Parse(Json(version: "1.2.3.4", notes: null));
        Assert.NotNull(manifest);
        Assert.Equal(new Version(1, 2, 3, 4), manifest.Version);
        Assert.Equal(string.Empty, manifest.Notes);
    }

    [Fact]
    public void TryParse_Utf8Bom_Accepted()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Json())).ToArray();
        Assert.NotNull(UpdateManifest.TryParse(bytes));
    }

    [Fact]
    public void TryParse_UnknownFields_Ignored() =>
        Assert.NotNull(Parse(Json().TrimEnd('}') + ",\"channel\":\"stable\",\"extra\":{\"a\":1}}"));

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("{not json")]
    [InlineData("{\"installer\":\"a.exe\",\"sha256\":\"" + Hash + "\"}")]
    public void TryParse_NotAManifest_ReturnsNull(string json) => Assert.Null(Parse(json));

    [Theory]
    [InlineData("1.2")]
    [InlineData("v1.2.3")]
    [InlineData("1.2.3-beta")]
    [InlineData("99999999999.0.0")]
    [InlineData("-1.0.0")]
    [InlineData(" 1.2.3")]
    public void TryParse_InvalidVersion_ReturnsNull(string version) => Assert.Null(Parse(Json(version: version)));

    [Theory]
    [InlineData("..\\evil.exe")]
    [InlineData("C:\\x.exe")]
    [InlineData("sub/setup.exe")]
    [InlineData("setup.msi")]
    [InlineData("")]
    [InlineData(".exe")]
    public void TryParse_InvalidInstaller_ReturnsNull(string installer) => Assert.Null(Parse(Json(installer: installer)));

    [Theory]
    [InlineData("ABCDEF0123456789abcdef0123456789ABCDEF0123456789abcdef012345678")]
    [InlineData("ZZCDEF0123456789abcdef0123456789ABCDEF0123456789abcdef0123456789")]
    [InlineData("")]
    public void TryParse_InvalidSha256_ReturnsNull(string sha256) => Assert.Null(Parse(Json(sha256: sha256)));

    [Fact]
    public void TryParse_NotesTooLong_ReturnsNull() => Assert.Null(Parse(Json(notes: new string('a', 501))));

    [Fact]
    public void TryParse_OverMaxBytes_ReturnsNull()
    {
        var padded = Json(notes: "x") + new string(' ', UpdateManifest.MaxBytes);
        Assert.Null(Parse(padded));
    }

    [Fact]
    public void TryParse_WrongFieldType_ReturnsNull() =>
        Assert.Null(Parse("{\"version\":123,\"installer\":\"a.exe\",\"sha256\":\"" + Hash + "\"}"));
}
