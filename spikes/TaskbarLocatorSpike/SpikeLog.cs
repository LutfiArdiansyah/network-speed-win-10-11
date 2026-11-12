using System.IO;

namespace TaskbarLocatorSpike;

/// <summary>Log spike ke %LOCALAPPDATA%\NetworkSpeedSpike. Tidak mencatat nama app/IP/SSID.</summary>
internal static class SpikeLog
{
    private static readonly object Gate = new();
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NetworkSpeedSpike",
        $"locator-{DateTime.Now:yyyyMMdd-HHmmss}.log");

    public static string PathForDisplay => FilePath;

    public static void Write(string message)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
    }
}
