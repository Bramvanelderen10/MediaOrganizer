namespace MediaOrganizer.Torrents;

/// <summary>
/// Abstraction over a BitTorrent client so the torrent endpoints can be unit-tested
/// without talking to a real qBittorrent instance.
/// </summary>
public interface ITorrentClient
{
    /// <summary>
    /// Adds a torrent metadata file and starts downloading it immediately.
    /// </summary>
    /// <param name="fileName">Original file name of the .torrent file, passed through to the client.</param>
    /// <param name="content">Raw bytes of the .torrent file.</param>
    /// <param name="savePath">Download folder as understood by the BitTorrent client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddTorrentFileAsync(
        string fileName,
        byte[] content,
        string savePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a torrent from a magnet link or .torrent URL and starts downloading it immediately.
    /// </summary>
    /// <param name="magnetLink">Magnet link (or http/https URL to a .torrent file).</param>
    /// <param name="savePath">Download folder as understood by the BitTorrent client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddTorrentUrlAsync(
        string magnetLink,
        string savePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current torrent list with progress information.
    /// </summary>
    Task<IReadOnlyList<TorrentInfo>> GetTorrentsAsync(
        CancellationToken cancellationToken = default);
}
