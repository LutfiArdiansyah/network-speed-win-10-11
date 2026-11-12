namespace NetworkSpeed.Core.Ipc;

/// <summary>Jeda reconnect ke service (spec S-06): 1, 2, 5, 10, lalu 30 detik seterusnya; <see cref="Reset"/> setelah berhasil.</summary>
public sealed class ReconnectBackoff
{
    private static readonly TimeSpan[] Steps =
    [
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30),
    ];

    private int _index;

    public TimeSpan NextDelay()
    {
        var delay = Steps[Math.Min(_index, Steps.Length - 1)];
        _index++;
        return delay;
    }

    public void Reset() => _index = 0;
}
