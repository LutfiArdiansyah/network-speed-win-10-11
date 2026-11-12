namespace NetworkSpeed.Core.Update;

/// <summary>
/// Validasi nilai <c>HKLM\SOFTWARE\NetworkSpeed\UpdateFolder</c> (diisi installer, spec §5, §10): harus path penuh
/// (UNC atau drive), tanpa segmen "..", tanpa karakter ilegal, maks 260 karakter.
/// </summary>
public static class UpdateFolderPath
{
    private const int MaxLength = 260;

    /// <returns>Path yang sudah dipangkas, atau <c>null</c> bila kosong/tidak valid (= cek update tidak dikonfigurasi).</returns>
    public static string? Normalize(string? raw)
    {
        var path = raw?.Trim();
        if (string.IsNullOrEmpty(path)
            || path.Length > MaxLength
            || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0
            || path.Contains('|', StringComparison.Ordinal)
            || !Path.IsPathFullyQualified(path))
        {
            return null;
        }

        var segments = path.Split('\\', '/');
        return segments.Any(s => s == "..") ? null : path;
    }
}
