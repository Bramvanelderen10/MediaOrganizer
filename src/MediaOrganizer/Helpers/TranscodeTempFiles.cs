namespace MediaOrganizer.Helpers;

/// <summary>
/// Identifies the sidecar files written while a transcode is in progress, for example
/// <c>Movie.transcode.mkv</c>. These files are incomplete, so discovery, cleanup, and the
/// transcoder itself must never treat them as library media.
/// </summary>
public static class TranscodeTempFiles
{
    /// <summary>Marker inserted between the file stem and its extension.</summary>
    public const string Marker = ".transcode";

    /// <summary>Builds the in-progress output path for <paramref name="filePath"/>.</summary>
    public static string BuildTempPath(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(filePath);
        return Path.Combine(directory, $"{stem}{Marker}{Path.GetExtension(filePath)}");
    }

    /// <summary>True when the path points at an in-progress transcode output.</summary>
    public static bool IsTempFile(string path)
        => Path.GetFileNameWithoutExtension(path)
            .EndsWith(Marker, StringComparison.OrdinalIgnoreCase);
}
