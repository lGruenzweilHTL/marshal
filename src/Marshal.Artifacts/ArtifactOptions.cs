namespace Marshal.Artifacts;

/// <summary>The <c>artifacts</c> section of <c>marshal.yaml</c>.</summary>
public sealed class ArtifactOptions
{
    /// <summary>Storage per owner in megabytes (default 5 GB).</summary>
    public int QuotaMb { get; set; } = 5120;

    /// <summary>Largest single file in megabytes, for uploads, executor output and attachments.</summary>
    public int MaxUploadMb { get; set; } = 200;

    /// <summary>Lifetime of signed content links in minutes.</summary>
    public int SignedLinkMinutes { get; set; } = 15;

    internal long QuotaBytes => QuotaMb * 1024L * 1024L;

    internal long MaxFileBytes => MaxUploadMb * 1024L * 1024L;
}

/// <summary>Directory holding artifact files (<c>/data/artifacts</c>), one folder per owner.</summary>
public sealed record ArtifactDirectory(string Path);
