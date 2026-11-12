namespace NetworkSpeed.Core.Sampling;

/// <summary>Kecepatan satu adapter dalam byte per detik.</summary>
public readonly record struct InterfaceRate(Guid InterfaceId, double ReceivedBytesPerSecond, double SentBytesPerSecond);
