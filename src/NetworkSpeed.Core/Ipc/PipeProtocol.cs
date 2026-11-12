using System.Buffers.Binary;
using System.Text.Json;

namespace NetworkSpeed.Core.Ipc;

/// <summary>Pesan pipe tidak valid (terlalu besar, terpotong).</summary>
public sealed class PipeProtocolException : Exception
{
    public PipeProtocolException()
    {
    }

    public PipeProtocolException(string message)
        : base(message)
    {
    }

    public PipeProtocolException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Protokol named pipe App ↔ service (spec S-04): pesan = panjang 4 byte little-endian + JSON UTF-8.
/// Batas ukuran diperiksa sebelum payload dibaca, agar klien nakal tidak bisa memaksa alokasi besar.
/// </summary>
public static class PipeProtocol
{
    public const string PipeName = "NetworkSpeed.Service.v1";
    public const int Version = 1;
    public const int MaxRequestBytes = 65_536;
    public const int MaxResponseBytes = 4 * 1024 * 1024;
    public const int MaxHistoryPoints = 10_080;
    public const int MaxHistoryDays = 62;
    public const int MaxAdapterIds = 64;
    private const int LengthPrefixBytes = 4;

    public static byte[] Serialize(PipeRequest request) => JsonSerializer.SerializeToUtf8Bytes(request, PipeJsonContext.Default.PipeRequest);

    public static byte[] Serialize(PipeResponse response) => JsonSerializer.SerializeToUtf8Bytes(response, PipeJsonContext.Default.PipeResponse);

    /// <returns><c>null</c> bila JSON tidak valid.</returns>
    public static PipeRequest? DeserializeRequest(ReadOnlySpan<byte> payload)
    {
        try
        {
            return JsonSerializer.Deserialize(payload, PipeJsonContext.Default.PipeRequest);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <returns><c>null</c> bila JSON tidak valid.</returns>
    public static PipeResponse? DeserializeResponse(ReadOnlySpan<byte> payload)
    {
        try
        {
            return JsonSerializer.Deserialize(payload, PipeJsonContext.Default.PipeResponse);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static async Task WriteAsync(Stream stream, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var prefix = new byte[LengthPrefixBytes];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, payload.Length);
        await stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <exception cref="PipeProtocolException">Panjang di luar batas atau aliran terputus.</exception>
    public static async Task<byte[]> ReadAsync(Stream stream, int maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            var prefix = new byte[LengthPrefixBytes];
            await stream.ReadExactlyAsync(prefix, cancellationToken).ConfigureAwait(false);
            var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
            if (length < 0 || length > maxBytes)
            {
                throw new PipeProtocolException("Message length is out of range.");
            }

            var payload = new byte[length];
            await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
            return payload;
        }
        catch (EndOfStreamException ex)
        {
            throw new PipeProtocolException("Message was truncated.", ex);
        }
    }
}
