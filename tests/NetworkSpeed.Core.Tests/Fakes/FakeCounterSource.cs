using NetworkSpeed.Core.Adapters;
using NetworkSpeed.Core.Sampling;

namespace NetworkSpeed.Core.Tests.Fakes;

internal sealed class FakeCounterSource : IInterfaceCounterSource
{
    public List<InterfaceCounterSnapshot> Counters { get; } = [];

    public List<AdapterInfo> Adapters { get; } = [];

    public int ReadAdaptersCalls { get; private set; }

    public IReadOnlyList<InterfaceCounterSnapshot> ReadCounters() => [.. Counters];

    public IReadOnlyList<AdapterInfo> ReadAdapters()
    {
        ReadAdaptersCalls++;
        return [.. Adapters];
    }

    public void Set(Guid id, ulong rx, ulong tx)
    {
        Counters.RemoveAll(c => c.InterfaceId == id);
        Counters.Add(new InterfaceCounterSnapshot(id, rx, tx));
    }
}
