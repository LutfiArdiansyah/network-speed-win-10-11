namespace NetworkSpeed.Core.Adapters;

/// <summary>Informasi adapter yang dibutuhkan untuk memilih adapter. Identitas selalu <see cref="Id"/>, bukan nama.</summary>
public sealed record AdapterInfo(Guid Id, string Name, AdapterKind Kind, bool IsUp, bool HasDefaultGateway);
