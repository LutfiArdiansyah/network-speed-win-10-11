using NetworkSpeed.Core.Update;

namespace NetworkSpeed.Core.Tests.Update;

public class UpdateFolderPathTests
{
    [Theory]
    [InlineData(@"\\server\share\NetworkSpeed", @"\\server\share\NetworkSpeed")]
    [InlineData(@"D:\Updates\NetworkSpeed", @"D:\Updates\NetworkSpeed")]
    [InlineData(@"  \\server\share\NetworkSpeed  ", @"\\server\share\NetworkSpeed")]
    public void Normalize_Valid_ReturnsTrimmed(string raw, string expected) =>
        Assert.Equal(expected, UpdateFolderPath.Normalize(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"relative\folder")]
    [InlineData(@"\rooted\no-drive")]
    [InlineData(@"\\server\share\..\other")]
    [InlineData(@"C:\a\..\b")]
    [InlineData(@"C:\bad|name")]
    public void Normalize_Invalid_ReturnsNull(string? raw) => Assert.Null(UpdateFolderPath.Normalize(raw));

    [Fact]
    public void Normalize_TooLong_ReturnsNull() =>
        Assert.Null(UpdateFolderPath.Normalize(@"\\server\share\" + new string('a', 260)));
}
