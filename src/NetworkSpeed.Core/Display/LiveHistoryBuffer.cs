namespace NetworkSpeed.Core.Display;

/// <summary>Kecepatan beberapa detik terakhir untuk tab "Live" (spec H-02), diisi dari sampling overlay; tanpa service.</summary>
public sealed class LiveHistoryBuffer(int capacity = 60)
{
    private readonly Queue<(double Up, double Down)> _samples = new(capacity);

    public void Add(double up, double down)
    {
        if (_samples.Count == capacity)
        {
            _samples.Dequeue();
        }

        _samples.Enqueue((up, down));
    }

    public IReadOnlyList<(double Up, double Down)> Snapshot() => [.. _samples];
}
