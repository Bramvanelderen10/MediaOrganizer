namespace MediaOrganizer.Configuration;

/// <summary>
/// Settings for the qBittorrent WebUI integration used to add .torrent files.
/// When <see cref="Url"/> is empty the torrent endpoints report the feature as unavailable.
/// </summary>
public class QbittorrentOptions
{
    /// <summary>Base URL of the qBittorrent WebUI, e.g. http://qbittorrent:8080.</summary>
    public string? Url { get; set; }

    /// <summary>WebUI username used to log in.</summary>
    public string? Username { get; set; }

    /// <summary>WebUI password used to log in.</summary>
    public string? Password { get; set; }

    /// <summary>
    /// Download folder sent to qBittorrent as the torrent save path.
    /// Falls back to <see cref="MediaOrganizerOptions.SourceFolder"/> when not set, so
    /// downloads land in the same mounted volume the organizer scans.
    /// </summary>
    public string? DownloadFolder { get; set; }

    /// <summary>Optional qBittorrent category applied to added torrents.</summary>
    public string? Category { get; set; }

    /// <summary>Optional comma separated tags applied to added torrents.</summary>
    public string? Tags { get; set; }

    /// <summary>Timeout in seconds for the login and add-torrent HTTP calls.</summary>
    public int RequestTimeoutSeconds { get; set; } = 60;
}
