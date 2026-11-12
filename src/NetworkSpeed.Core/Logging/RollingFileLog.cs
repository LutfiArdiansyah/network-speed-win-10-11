using System.Globalization;

namespace NetworkSpeed.Core.Logging;

/// <summary>Logger aplikasi. Jangan pernah mengirim IP, SSID, host, nama proses, atau path pengguna (spec §7).</summary>
public interface IAppLog
{
    void Info(string message);

    void Warn(string message);

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming",
        "CA1716:Identifiers should not match keywords",
        Justification = "Proyek hanya C#; nama Error konsisten dengan Info/Warn.")]
    void Error(string message, Exception? exception = null);
}

/// <summary>
/// Log teks dengan rotasi ukuran: <c>&lt;baseName&gt;.log</c>, <c>&lt;baseName&gt;.1.log</c> … maksimal <c>maxFiles</c> file.
/// Pesan exception tidak ditulis (bisa memuat path/IP); hanya tipe, HResult, dan stack trace.
/// </summary>
public sealed class RollingFileLog : IAppLog
{
    private readonly string _baseName;
    private readonly string _directory;
    private readonly long _maxBytes;
    private readonly int _maxFiles;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();

    /// <param name="baseName">
    /// Nama file tanpa ekstensi (huruf, angka, '-'). Proses berbeda wajib memakai nama berbeda: rotasi tidak aman
    /// lintas proses.
    /// </param>
    public RollingFileLog(string directory, long maxBytes = 1_048_576, int maxFiles = 5, TimeProvider? time = null, string baseName = "app")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(baseName);
        if (baseName.Length == 0 || !baseName.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
        {
            throw new ArgumentException("Log base name may contain only letters, digits and '-'.", nameof(baseName));
        }

        _baseName = baseName;
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFiles, 1);
        _directory = directory;
        _maxBytes = maxBytes;
        _maxFiles = maxFiles;
        _time = time ?? TimeProvider.System;
    }

    private string CurrentPath => Path.Combine(_directory, _baseName + ".log");

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message, Exception? exception = null)
    {
        var detail = exception is null
            ? string.Empty
            : $" | {exception.GetType().Name} 0x{exception.HResult:X8} | {Sanitize(exception.StackTrace ?? string.Empty)}";
        Write("ERROR", message + detail);
    }

    private static string Sanitize(string text) => text.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

    private void Write(string level, string message)
    {
        var line = $"{_time.GetLocalNow().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} {level} {Sanitize(message)}{Environment.NewLine}";
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                var info = new FileInfo(CurrentPath);
                if (info.Exists && info.Length + line.Length > _maxBytes)
                {
                    Rotate();
                }

                File.AppendAllText(CurrentPath, line);
            }
            catch (IOException)
            {
                // Logging tidak boleh membuat aplikasi crash; tidak ada tempat lain untuk melapor.
            }
            catch (UnauthorizedAccessException)
            {
                // Sama seperti di atas.
            }
        }
    }

    private void Rotate()
    {
        var oldest = RotatedPath(_maxFiles - 1);
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var i = _maxFiles - 2; i >= 1; i--)
        {
            var source = RotatedPath(i);
            if (File.Exists(source))
            {
                File.Move(source, RotatedPath(i + 1));
            }
        }

        if (_maxFiles > 1)
        {
            File.Move(CurrentPath, RotatedPath(1));
        }
        else
        {
            File.Delete(CurrentPath);
        }
    }

    private string RotatedPath(int index) =>
        Path.Combine(_directory, $"{_baseName}.{index.ToString(CultureInfo.InvariantCulture)}.log");
}
