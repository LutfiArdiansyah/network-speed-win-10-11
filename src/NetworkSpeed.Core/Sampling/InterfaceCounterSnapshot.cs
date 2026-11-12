namespace NetworkSpeed.Core.Sampling;

/// <summary>Nilai counter byte kumulatif satu adapter pada satu waktu.</summary>
public readonly record struct InterfaceCounterSnapshot(Guid InterfaceId, ulong BytesReceived, ulong BytesSent);
