using NetworkSpeed.Core.Adapters;

namespace NetworkSpeed.Interop.Tests;

public class IfTableCounterSourceTests
{
    [Fact]
    public void ReadCounters_ReturnsUniqueInterfaceIds()
    {
        var rows = new IfTableCounterSource().ReadCounters();
        Assert.NotEmpty(rows);
        Assert.Equal(rows.Count, rows.Select(r => r.InterfaceId).Distinct().Count());
    }

    [Fact]
    public void ReadCounters_TwoReads_CountersNeverDecreaseForStableInterface()
    {
        var source = new IfTableCounterSource();
        var first = source.ReadCounters().ToDictionary(r => r.InterfaceId);
        var second = source.ReadCounters();
        foreach (var row in second.Where(r => first.ContainsKey(r.InterfaceId)))
        {
            Assert.True(row.BytesReceived >= first[row.InterfaceId].BytesReceived);
            Assert.True(row.BytesSent >= first[row.InterfaceId].BytesSent);
        }
    }

    [Fact]
    public void ReadAdapters_IdsMatchCounters_AndLoopbackClassified()
    {
        var source = new IfTableCounterSource();
        var adapters = source.ReadAdapters();
        Assert.Equal(
            source.ReadCounters().Select(c => c.InterfaceId).ToHashSet(),
            adapters.Select(a => a.Id).ToHashSet());
        Assert.Contains(adapters, a => a.Kind == AdapterKind.Loopback);
    }
}
