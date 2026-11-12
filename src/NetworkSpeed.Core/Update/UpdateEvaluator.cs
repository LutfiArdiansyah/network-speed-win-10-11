namespace NetworkSpeed.Core.Update;

/// <summary>Hasil perbandingan versi terpasang dengan <c>latest.json</c>.</summary>
public enum UpdateState
{
    UpToDate,
    Available,
}

/// <summary>Membandingkan versi; bagian yang tidak ada (mis. Revision pada "1.2.3") dianggap 0.</summary>
public static class UpdateEvaluator
{
    public static UpdateState Evaluate(Version current, UpdateManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(manifest);
        return Normalize(manifest.Version) > Normalize(current) ? UpdateState.Available : UpdateState.UpToDate;
    }

    private static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
}
