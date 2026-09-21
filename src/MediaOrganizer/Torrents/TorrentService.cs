using System.Text;

using MediaOrganizer.Configuration;

using Microsoft.Extensions.Options;

namespace MediaOrganizer.Torrents;

/// <summary>
/// Validates an uploaded torrent file and forwards it to the configured BitTorrent client.
/// </summary>
/// <remarks>
/// This service deliberately never triggers the organize job. Files downloaded into the
/// source folder are organized the next time the organize job is run.
/// </remarks>
public class TorrentService
{
    private readonly ILogger<TorrentService> _logger;
    private readonly MediaOrganizerOptions _options;
    private readonly ITorrentClient _torrentClient;

    public TorrentService(
        ILogger<TorrentService> logger,
        IOptions<MediaOrganizerOptions> options,
        ITorrentClient torrentClient)
    {
        _logger = logger;
        _options = options.Value;
        _torrentClient = torrentClient;
    }

    /// <summary>
    /// Whether a qBittorrent instance has been configured.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Qbittorrent.Url);

    /// <summary>
    /// Validates the upload, resolves the download folder and adds the torrent to the client.
    /// </summary>
    public async Task<AddTorrentResult> AddTorrentAsync(
        string? fileName,
        byte[] content,
        string? folderPathOverride = null,
        CancellationToken cancellationToken = default)
    {
        ValidateUpload(fileName, content);

        // Resolve (and validate) the request fully before reporting server availability,
        // so malformed requests always get a 400 rather than a misleading 503.
        var savePath = ResolveDownloadFolder(folderPathOverride);

        if (!IsConfigured)
        {
            throw new QbittorrentNotConfiguredException(
                "qBittorrent is not configured. Set MediaOrganizer:Qbittorrent:Url in appsettings.");
        }

        await _torrentClient.AddTorrentFileAsync(fileName!, content, savePath, cancellationToken);

        return new AddTorrentResult(Path.GetFileName(fileName!), savePath, content.Length);
    }

    /// <summary>
    /// Resolves the folder the torrent should download to.
    /// Priority: request override, then <c>Qbittorrent:DownloadFolder</c>,
    /// then <c>SourceFolder</c> so downloads land in the same mounted volume.
    /// </summary>
    public string ResolveDownloadFolder(string? folderPathOverride = null)
    {
        if (!string.IsNullOrWhiteSpace(folderPathOverride))
        {
            var candidate = folderPathOverride.Trim();

            if (!Path.IsPathRooted(candidate))
            {
                throw new TorrentValidationException("folderPath must be an absolute path.");
            }

            if (candidate.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains(".."))
            {
                throw new TorrentValidationException("folderPath must not contain parent directory segments.");
            }

            return candidate;
        }

        if (!string.IsNullOrWhiteSpace(_options.Qbittorrent.DownloadFolder))
        {
            return _options.Qbittorrent.DownloadFolder.Trim();
        }

        if (!string.IsNullOrWhiteSpace(_options.SourceFolder))
        {
            return _options.SourceFolder.Trim();
        }

        throw new TorrentValidationException(
            "No download folder is configured. Set MediaOrganizer:Qbittorrent:DownloadFolder or MediaOrganizer:SourceFolder.");
    }

    /// <summary>
    /// Validates a magnet link, resolves the download folder and adds it to the client.
    /// </summary>
    public async Task<AddTorrentResult> AddMagnetAsync(
        string? magnetLink,
        string? folderPathOverride = null,
        CancellationToken cancellationToken = default)
    {
        var link = ValidateMagnetLink(magnetLink);

        // Resolve (and validate) the request fully before reporting server availability,
        // so malformed requests always get a 400 rather than a misleading 503.
        var savePath = ResolveDownloadFolder(folderPathOverride);

        if (!IsConfigured)
        {
            throw new QbittorrentNotConfiguredException(
                "qBittorrent is not configured. Set MediaOrganizer:Qbittorrent:Url in appsettings.");
        }

        await _torrentClient.AddTorrentUrlAsync(link, savePath, cancellationToken);

        return new AddTorrentResult(DescribeMagnet(link), savePath, 0);
    }

    /// <summary>
    /// Returns the torrents known to the BitTorrent client with their progress.
    /// </summary>
    public Task<IReadOnlyList<TorrentInfo>> GetTorrentsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            throw new QbittorrentNotConfiguredException(
                "qBittorrent is not configured. Set MediaOrganizer:Qbittorrent:Url in appsettings.");
        }

        return _torrentClient.GetTorrentsAsync(cancellationToken);
    }

    /// <summary>
    /// Accepts <c>magnet:?xt=urn:btih:…</c> links and http(s) URLs pointing at a .torrent file.
    /// </summary>
    internal static string ValidateMagnetLink(string? magnetLink)
    {
        if (string.IsNullOrWhiteSpace(magnetLink))
        {
            throw new TorrentValidationException("A magnet link is required.");
        }

        var link = magnetLink.Trim();

        if (link.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(link, UriKind.Absolute, out var magnetUri)
                || magnetUri.Query.Length <= 1)
            {
                throw new TorrentValidationException("The magnet link is not valid.");
            }

            if (!link.Contains("xt=urn:btih:", StringComparison.OrdinalIgnoreCase))
            {
                throw new TorrentValidationException(
                    "The magnet link does not contain a BitTorrent info hash (xt=urn:btih:).");
            }

            return link;
        }

        if (Uri.TryCreate(link, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return link;
        }

        throw new TorrentValidationException(
            "Provide a magnet link (magnet:?xt=urn:btih:...) or an http(s) URL to a .torrent file.");
    }

    /// <summary>
    /// Builds a friendly display name for a magnet link: the <c>dn</c> parameter when present,
    /// otherwise a shortened info hash.
    /// </summary>
    private static string DescribeMagnet(string magnetLink)
    {
        var displayName = ExtractQueryValue(magnetLink, "dn");
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return Path.GetFileName(displayName.Replace('\\', '/')).Trim();
        }

        var hash = ExtractBtihHash(magnetLink);
        return string.IsNullOrWhiteSpace(hash) ? "magnet link" : $"magnet:{hash}";
    }

    private static string? ExtractQueryValue(string magnetLink, string key)
    {
        var start = magnetLink.IndexOf('?');
        if (start < 0)
        {
            return null;
        }

        foreach (var pair in magnetLink[(start + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            if (pair[..separator].Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pair[(separator + 1)..].Replace('+', ' '));
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts the raw 40 character info hash, accepting both the hex and base32 forms.
    /// </summary>
    private static string? ExtractBtihHash(string magnetLink)
    {
        const string marker = "xt=urn:btih:";
        var index = magnetLink.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var rest = magnetLink[(index + marker.Length)..];
        var end = rest.IndexOf('&');
        return end < 0 ? rest : rest[..end];
    }

    private static void ValidateUpload(string? fileName, byte[] content)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new TorrentValidationException("A torrent file name is required.");
        }

        if (!fileName.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
        {
            throw new TorrentValidationException($"'{fileName}' is not a .torrent file.");
        }

        if (content is null || content.Length == 0)
        {
            throw new TorrentValidationException("The uploaded torrent file is empty.");
        }

        if (!LooksLikeTorrentFile(content))
        {
            throw new TorrentValidationException("The uploaded file is not a valid torrent file.");
        }
    }

    /// <summary>
    /// Cheap sanity check that the bytes look like a bencoded torrent file
    /// (a dictionary containing the mandatory <c>info</c> key).
    /// </summary>
    private static bool LooksLikeTorrentFile(byte[] content)
    {
        if (content.Length == 0 || content[0] != (byte)'d')
        {
            return false;
        }

        return Encoding.Latin1.GetString(content).Contains("4:info", StringComparison.Ordinal);
    }
}

/// <summary>
/// Details of a torrent that was accepted by the BitTorrent client.
/// </summary>
public record AddTorrentResult(string FileName, string SavePath, int SizeBytes);
