using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using NetworkSpeed.Core.Ipc;
using NetworkSpeed.Core.Logging;

namespace NetworkSpeed.Service.Ipc;

/// <summary>Memproses satu request yang sudah tervalidasi. <paramref name="callerSessionId"/> berasal dari OS, bukan dari klien.</summary>
public interface IPipeRequestHandler
{
    PipeResponse Handle(PipeRequest request, int callerSessionId);
}

/// <summary>
/// Named pipe read-only untuk App (spec S-04, S-08):
/// <list type="bullet">
/// <item>ACL: Interactive Users boleh baca/tulis pesan, SYSTEM/Administrators/akun service penuh, Network ditolak.</item>
/// <item>Satu request per koneksi, maksimal <c>maxInstances</c> koneksi bersamaan, timeout per request.</item>
/// <item>Request divalidasi sebelum diproses; error ke klien hanya berupa kode, tanpa detail internal.</item>
/// <item>Sesi pemanggil diambil dari <c>GetNamedPipeClientSessionId</c> (spec S-05).</item>
/// </list>
/// </summary>
public sealed class PipeServer(
    string pipeName,
    IPipeRequestHandler handler,
    IAppLog log,
    int maxInstances = 16,
    TimeSpan? requestTimeout = null)
{
    private readonly TimeSpan _timeout = requestTimeout ?? TimeSpan.FromSeconds(5);

    /// <summary>ACL pipe. Publik agar bisa diverifikasi oleh test.</summary>
    public static PipeSecurity CreateSecurity()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.InteractiveSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));

        // Akun proses ini (LocalService) perlu hak penuh untuk membuat instance pipe berikutnya.
        using var identity = WindowsIdentity.GetCurrent();
        security.AddAccessRule(new PipeAccessRule(identity.User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        return security;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var listeners = Enumerable.Range(0, maxInstances).Select(i => ListenAsync(isFirst: i == 0, cancellationToken)).ToList();
        await Task.WhenAll(listeners).ConfigureAwait(false);
    }

    private async Task ListenAsync(bool isFirst, CancellationToken cancellationToken)
    {
        var firstInstance = isFirst;
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                // FirstPipeInstance pada instance pertama: gagal bila proses lain sudah memakai nama ini (squatting).
                var options = PipeOptions.Asynchronous | (firstInstance ? PipeOptions.FirstPipeInstance : PipeOptions.None);
                pipe = NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, maxInstances, PipeTransmissionMode.Byte, options, 0, 0, CreateSecurity());
                firstInstance = false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Error("Creating pipe instance failed.", ex);
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                continue;
            }

            await using (pipe.ConfigureAwait(false))
            {
                try
                {
                    await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                    await ServeAsync(pipe, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (IOException)
                {
                    // Klien memutus koneksi; lanjut ke koneksi berikutnya.
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // Satu koneksi bermasalah (klien tidak membaca jawaban → write timeout, putus sebelum sesi terbaca, dll.)
                    // tidak boleh mematikan listener; kalau semua listener mati, service ikut berhenti.
                    log.Warn($"Pipe connection aborted ({ex.GetType().Name}).");
                }
            }
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        PipeResponse response;
        try
        {
            var payload = await PipeProtocol.ReadAsync(pipe, PipeProtocol.MaxRequestBytes, timeout.Token).ConfigureAwait(false);
            response = Process(payload, CallerSessionId(pipe));
        }
        catch (PipeProtocolException)
        {
            log.Warn("Rejected malformed or oversized pipe message.");
            return;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            log.Warn("Pipe client timed out.");
            return;
        }

        await PipeProtocol.WriteAsync(pipe, PipeProtocol.Serialize(response), timeout.Token).ConfigureAwait(false);
    }

    private PipeResponse Process(byte[] payload, int callerSessionId)
    {
        if (PipeProtocol.DeserializeRequest(payload) is not { } request)
        {
            return PipeResponse.Error(PipeErrors.InvalidRequest);
        }

        if (PipeRequestValidator.Validate(request) is { } error)
        {
            return PipeResponse.Error(error);
        }

        try
        {
            return handler.Handle(request, callerSessionId);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.Error("Pipe request handling failed.", ex);
            return PipeResponse.Error(PipeErrors.Internal);
        }
    }

    private static int CallerSessionId(NamedPipeServerStream pipe)
    {
        if (!GetNamedPipeClientSessionId(pipe.SafePipeHandle, out var sessionId))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return (int)sessionId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientSessionId(SafePipeHandle pipe, out uint sessionId);
}
