namespace MediaOrganizer.Torrents;

/// <summary>
/// Raised when the configured BitTorrent client could not be reached or rejected the request.
/// The message is safe to surface to API callers.
/// </summary>
public class QbittorrentException : Exception
{
    public QbittorrentException(string message)
        : base(message)
    {
    }

    public QbittorrentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised when no qBittorrent instance has been configured.
/// </summary>
public class QbittorrentNotConfiguredException : QbittorrentException
{
    public QbittorrentNotConfiguredException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Raised when an uploaded file is not a usable torrent file.
/// </summary>
public class TorrentValidationException : Exception
{
    public TorrentValidationException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// A single torrent as reported by the BitTorrent client, with the fields needed to
/// render a progress list.
/// </summary>
public record TorrentInfo(
    string Hash,
    string Name,
    /// <summary>Raw client state, e.g. <c>stalledDL</c>.</summary>
    string State,
    /// <summary>Human readable state derived from <see cref="State"/>.</summary>
    string Status,
    /// <summary>Completion ratio between 0 and 1.</summary>
    double Progress,
    long SizeBytes,
    long DownloadedBytes,
    long AmountLeftBytes,
    long DownloadSpeed,
    long UploadSpeed,
    /// <summary>Seconds until completion, or null when unknown / not downloading.</summary>
    long? EtaSeconds,
    string SavePath,
    long AddedOnUnixSeconds)
{
    /// <summary>True while the torrent still has data left to download.</summary>
    public bool IsDownloading => AmountLeftBytes > 0;
}
