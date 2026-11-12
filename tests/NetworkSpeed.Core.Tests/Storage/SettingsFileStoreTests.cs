using NetworkSpeed.Core.Settings;
using NetworkSpeed.Core.Storage;
using NetworkSpeed.Core.Tests.Fakes;

namespace NetworkSpeed.Core.Tests.Storage;

public sealed class SettingsFileStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ns-settings-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTime _time = new(new DateTimeOffset(2026, 11, 12, 10, 0, 0, TimeSpan.Zero));

    public SettingsFileStoreTests() => Directory.CreateDirectory(_dir);

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Load_MissingFile_ReturnsDefaultsAndMissing()
    {
        var (settings, outcome) = new SettingsFileStore(SettingsPath, _time).Load();
        Assert.Equal(new UserSettings(), settings);
        Assert.Equal(SettingsLoadOutcome.Missing, outcome);
    }

    [Fact]
    public void Load_GarbageFile_RenamesCorruptAndReturnsDefaults()
    {
        File.WriteAllText(SettingsPath, "{{{ not json");
        var (settings, outcome) = new SettingsFileStore(SettingsPath, _time).Load();
        Assert.Equal(SettingsLoadOutcome.CorruptRecovered, outcome);
        Assert.Equal(new UserSettings(), settings);
        Assert.False(File.Exists(SettingsPath));
        Assert.True(File.Exists(SettingsPath + ".corrupt-20261112100000"));
    }

    [Fact]
    public void Load_FourthCorrupt_KeepsOnlyThreeNewest()
    {
        var store = new SettingsFileStore(SettingsPath, _time);
        for (var i = 0; i < 4; i++)
        {
            File.WriteAllText(SettingsPath, "garbage");
            store.Load();
            _time.Advance(TimeSpan.FromMinutes(1));
        }

        var corrupt = Directory.GetFiles(_dir, "settings.json.corrupt-*").Select(Path.GetFileName).Order().ToList();
        Assert.Equal(
            ["settings.json.corrupt-20261112100100", "settings.json.corrupt-20261112100200", "settings.json.corrupt-20261112100300"],
            corrupt);
    }

    [Fact]
    public void Load_TwoCorruptInSameSecond_BothKept()
    {
        var store = new SettingsFileStore(SettingsPath, _time);
        File.WriteAllText(SettingsPath, "garbage");
        store.Load();
        File.WriteAllText(SettingsPath, "garbage");
        store.Load();
        Assert.Equal(2, Directory.GetFiles(_dir, "settings.json.corrupt-*").Length);
    }

    [Fact]
    public void Save_ThenLoad_ReturnsSameSettings()
    {
        var store = new SettingsFileStore(Path.Combine(_dir, "nested", "settings.json"), _time);
        var s = new UserSettings { FontSize = 13, Hotkey = "Ctrl+Alt+K" };
        store.Save(s);
        var (loaded, outcome) = store.Load();
        Assert.Equal(SettingsLoadOutcome.Loaded, outcome);
        Assert.Equal(s, loaded);
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "nested"), "*.tmp"));
    }
}
