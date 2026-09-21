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
