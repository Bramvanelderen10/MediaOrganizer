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
}
