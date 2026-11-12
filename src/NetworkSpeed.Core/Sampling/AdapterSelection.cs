using NetworkSpeed.Core.Adapters;

namespace NetworkSpeed.Core.Sampling;

/// <summary>Pilihan adapter pengguna yang diteruskan ke <see cref="AdapterSelector"/> (spec F-04, F-05).</summary>
public sealed record AdapterSelection(AdapterSelectionMode Mode, Guid? SpecificId, bool IncludeVpnInAll);
