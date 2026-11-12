using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using NetworkSpeed.Core.Display;

namespace NetworkSpeed.Interop.Net;

/// <summary>Ping ICMP untuk latency (spec X-01). Host tidak boleh dicatat ke log (spec §7).</summary>
public static class LatencyProbe
{
    /// <param name="host">Hostname/IP; <c>null</c> = default gateway IPv4 pertama yang aktif.</param>
    /// <returns><c>RoundTrip</c> null bila timeout, host tidak bisa di-resolve, atau tidak ada gateway.</returns>
    public static async Task<LatencyReading> PingAsync(string? host, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var target = host ?? DefaultGateway()?.ToString();
        if (target is null)
        {
            return new LatencyReading(null);
        }

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(target, timeout, cancellationToken: cancellationToken).ConfigureAwait(false);
            return reply.Status == IPStatus.Success
                ? new LatencyReading(TimeSpan.FromMilliseconds(reply.RoundtripTime))
                : new LatencyReading(null);
        }
        catch (PingException)
        {
            // DNS gagal atau jaringan tidak tersedia: tampil "timeout", bukan error (X-01).
            return new LatencyReading(null);
        }
    }

    private static IPAddress? DefaultGateway() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().GatewayAddresses)
            .Select(g => g.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
}
