using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NetworkSpeed.Core.Update;

/// <summary>
/// Isi <c>latest.json</c> di shared folder update (spec §10). File ditulis admin rilis, tetapi tetap diperlakukan sebagai
/// input luar: ukuran dibatasi, versi harus angka murni, dan nama installer tidak boleh memuat path.
/// </summary>
public sealed partial record UpdateManifest(Version Version, string InstallerFileName, string Sha256, string Notes)
{
    /// <summary>Ukuran maksimum <c>latest.json</c> (byte).</summary>
    public const int MaxBytes = 16 * 1024;

    private const int MaxNotesLength = 500;

    /// <returns><c>null</c> bila bukan manifest yang valid.</returns>
    public static UpdateManifest? TryParse(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.Length == 0 || utf8Json.Length > MaxBytes)
        {
            return null;
        }

        ReadOnlySpan<byte> bom = [0xEF, 0xBB, 0xBF];
        if (utf8Json.StartsWith(bom))
        {
            utf8Json = utf8Json[bom.Length..];
        }

        try
        {
            var reader = new Utf8JsonReader(utf8Json);
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || ReadString(root, "version") is not { } versionText
                || ReadString(root, "installer") is not { } installer
                || ReadString(root, "sha256") is not { } sha256)
            {
                return null;
            }

            var notes = root.TryGetProperty("notes", out var notesElement) && notesElement.ValueKind == JsonValueKind.String
                ? notesElement.GetString() ?? string.Empty
                : string.Empty;

            if (!VersionPattern().IsMatch(versionText)
                || !Version.TryParse(versionText, out var version)
                || !InstallerPattern().IsMatch(installer)
                || !Sha256Pattern().IsMatch(sha256)
                || notes.Length > MaxNotesLength)
            {
                return null;
            }

            return new UpdateManifest(version, installer, sha256.ToLower(CultureInfo.InvariantCulture), notes);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // Maks 9 digit per bagian agar muat di int (Version melempar/menolak angka lebih besar).
    [GeneratedRegex(@"^\d{1,9}(\.\d{1,9}){2,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    // Hanya nama file: tanpa pemisah path, drive, atau "..".
    [GeneratedRegex(@"^[A-Za-z0-9_-][A-Za-z0-9._-]{0,99}\.exe$", RegexOptions.CultureInvariant)]
    private static partial Regex InstallerPattern();

    [GeneratedRegex("^[0-9A-Fa-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}
