using System.IO;
using System.Windows.Threading;
using NetworkSpeed.Core.Logging;
using NetworkSpeed.Core.Storage;
using NetworkSpeed.Core.Update;

namespace NetworkSpeed.App.Features;

/// <summary>Membaca <c>latest.json</c> dari folder update (produksi: file, test: palsu).</summary>
public interface IManifestReader
{
    /// <returns>Isi file (maks <see cref="UpdateManifest.MaxBytes"/> + 1 byte agar file terlalu besar terdeteksi).</returns>
    Task<byte[]?> ReadAsync(string folder, CancellationToken cancellationToken);
}

/// <summary>
/// Pembaca produksi. Akses UNC bisa menggantung lama (VPN mati), jadi dijalankan di thread pool; pemanggil membatasi
/// waktunya dan meninggalkan thread yang menggantung.
/// </summary>
public sealed class FileManifestReader : IManifestReader
{
    private const string FileName = "latest.json";

    public Task<byte[]?> ReadAsync(string folder, CancellationToken cancellationToken) => Task.Run<byte[]?>(
        () =>
        {
            using var stream = new FileStream(Path.Combine(folder, FileName), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[UpdateManifest.MaxBytes + 1];
            var total = 0;
            int read;
            while (total < buffer.Length && (read = stream.Read(buffer, total, buffer.Length - total)) > 0)
            {
                total += read;
            }

            return buffer[..total];
        },
        cancellationToken);
}

/// <summary>Hasil satu kali cek update.</summary>
public enum UpdateCheckResult
{
    Disabled,
    NotConfigured,
    Unreachable,
    Invalid,
    UpToDate,
    Available,
}

/// <summary>
/// Cek versi baru di shared folder IT (spec §10): 30 detik setelah start lalu tiap 24 jam. Hanya informasi: toast sekali per
/// versi per user (<c>state.json</c>, kunci <c>update:&lt;versi&gt;</c>) dan status di Settings. Kegagalan apa pun diam
/// (tanpa dialog); log hanya memuat hasil, tidak pernah path folder. Dipanggil dari UI thread.
/// </summary>
public sealed class UpdateMonitor(
    IManifestReader reader,
    Func<string?> updateFolder,
    Func<bool> isEnabled,
    Version current,
    NotificationStateStore state,
    Action<string, string> showBalloon,
    IAppLog log,
    TimeSpan? readTimeout = null) : IDisposable
{
    private const string BalloonTitle = "Network Speed update";
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly TimeSpan _readTimeout = readTimeout ?? TimeSpan.FromSeconds(10);
    private readonly CancellationTokenSource _stop = new();
    private DispatcherTimer? _timer;
    private bool _checking;

    /// <summary>Dipicu setelah <see cref="StatusText"/> berubah (untuk jendela Settings).</summary>
    public event Action? StatusChanged;

    public string StatusText { get; private set; } = "Not checked yet";

    public void Start()
    {
        _timer = new DispatcherTimer { Interval = FirstCheckDelay };
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = Interval;
            await CheckAsync(_stop.Token).ConfigureAwait(true);
        };
        _timer.Start();
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        if (_checking)
        {
            return UpdateCheckResult.Disabled;
        }

        _checking = true;
        try
        {
            var result = await RunCheckAsync(cancellationToken).ConfigureAwait(true);
            log.Info($"Update check: {result}.");
            return result;
        }
        finally
        {
            _checking = false;
        }
    }

    public void Dispose()
    {
        _timer?.Stop();
        _stop.Cancel();
        _stop.Dispose();
    }

    private async Task<UpdateCheckResult> RunCheckAsync(CancellationToken cancellationToken)
    {
        if (!isEnabled())
        {
            SetStatus("Checking for new versions is turned off");
            return UpdateCheckResult.Disabled;
        }

        if (UpdateFolderPath.Normalize(updateFolder()) is not { } folder)
        {
            SetStatus("Not configured by IT");
            return UpdateCheckResult.NotConfigured;
        }

        byte[]? content;
        try
        {
            content = await reader.ReadAsync(folder, cancellationToken).WaitAsync(_readTimeout, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException or System.Security.SecurityException)
        {
            SetStatus("Update folder not reachable");
            return UpdateCheckResult.Unreachable;
        }

        if (content is null || UpdateManifest.TryParse(content) is not { } manifest)
        {
            SetStatus("Update information is not valid");
            return UpdateCheckResult.Invalid;
        }

        var version = Format(manifest.Version);
        if (UpdateEvaluator.Evaluate(current, manifest) == UpdateState.UpToDate)
        {
            SetStatus($"Up to date ({Format(current)})");
            return UpdateCheckResult.UpToDate;
        }

        SetStatus($"Version {version} is available. Your IT team will install it.");
        NotifyOnce(version);
        return UpdateCheckResult.Available;
    }

    private void NotifyOnce(string version)
    {
        var key = "update:" + version;
        try
        {
            if (state.Load().Contains(key))
            {
                return;
            }

            showBalloon(BalloonTitle, $"A new version ({version}) is available. Your IT team will install it.");
            state.MarkSent([key]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // state.json terkunci: coba lagi pada cek berikutnya (toast bisa tertunda, tidak berulang tanpa batas).
            log.Error("Update notification state could not be read or saved.", ex);
        }
    }

    private void SetStatus(string text)
    {
        StatusText = text;
        StatusChanged?.Invoke();
    }

    // Tampilkan x.y.z; bagian keempat hanya bila dipakai.
    private static string Format(Version v) => v.Revision > 0 ? v.ToString(4) : v.ToString(3);
}
