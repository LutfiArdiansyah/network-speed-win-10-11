using NetworkSpeed.Core.Adapters;

namespace NetworkSpeed.Core.Sampling;

/// <summary>Sumber counter byte dan informasi adapter dari sistem operasi.</summary>
public interface IInterfaceCounterSource
{
    /// <summary>Counter kumulatif setiap adapter yang bukan filter interface.</summary>
    IReadOnlyList<InterfaceCounterSnapshot> ReadCounters();

    /// <summary>Informasi adapter dengan ID yang sama dengan <see cref="ReadCounters"/>.</summary>
    /// <remarks>
    /// Mahal (GetIfTable2 + GetAdaptersAddresses untuk setiap adapter). Jangan dipanggil setiap tick sampling;
    /// simpan hasilnya dan segarkan saat event <c>NetworkChange</c> atau paling sering tiap beberapa detik,
    /// agar target CPU &lt; 1% (spec §1) tetap terpenuhi. <see cref="ReadCounters"/> murah dan aman dipanggil tiap tick.
    /// </remarks>
    IReadOnlyList<AdapterInfo> ReadAdapters();
}
