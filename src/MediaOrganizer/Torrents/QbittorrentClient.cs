using System.Net;
using System.Net.Http.Headers;

using MediaOrganizer.Configuration;

using Microsoft.Extensions.Options;

namespace MediaOrganizer.Torrents;

/// <summary>
/// Talks to the qBittorrent WebUI API (v2) to add torrent files.
/// </summary>
/// <remarks>
/// The WebUI validates the <c>Origin</c>/<c>Referer</c> header against the <c>Host</c> header,
/// so every request sets a matching referrer. Authentication uses the session cookie returned
/// by <c>/api/v2/auth/login</c>, which is cached for the lifetime of this singleton and
/// re-acquired once when the session expires. qBittorrent 4.x names the cookie <c>SID</c> and
/// replies <c>200 Ok.</c>; 5.x names it <c>QBT_SID_&lt;port&gt;</c> and replies <c>204</c> with
/// no body, so both shapes are handled.
/// </remarks>
public class QbittorrentClient : ITorrentClient
{
    private const string LoginPath = "api/v2/auth/login";
    private const string AddTorrentPath = "api/v2/torrents/add";
    private const string TorrentMimeType = "application/x-bittorrent";
    private const string FailedBody = "Fails.";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly QbittorrentOptions _options;
    private readonly ILogger<QbittorrentClient> _logger;

    private readonly SemaphoreSlim _loginLock = new(1, 1);
    private string? _sessionCookie;

    public QbittorrentClient(
        IHttpClientFactory httpClientFactory,
        IOptions<MediaOrganizerOptions> options,
        ILogger<QbittorrentClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value.Qbittorrent;
        _logger = logger;
    }

    public async Task AddTorrentFileAsync(
        string fileName,
        byte[] content,
        string savePath,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = GetBaseUrl();
        var client = CreateClient();

        var response = await SendAddTorrentAsync(client, baseUrl, fileName, content, savePath, cancellationToken);

        // The cached session may have expired (e.g. qBittorrent restarted). Log in again and retry once.
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            response.Dispose();
            _logger.LogInformation("qBittorrent session expired while adding {FileName}; re-authenticating", fileName);

            _sessionCookie = null;
            response = await SendAddTorrentAsync(client, baseUrl, fileName, content, savePath, cancellationToken);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.UnsupportedMediaType)
            {
                throw new QbittorrentException("qBittorrent rejected the torrent file as invalid.");
            }

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                throw new QbittorrentException("qBittorrent already has this torrent.");
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new QbittorrentException(
                    "qBittorrent refused the request. Check the configured username and password.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new QbittorrentException(
                    $"qBittorrent returned {(int)response.StatusCode} {response.ReasonPhrase} while adding the torrent.");
            }

            var body = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();

            // Older WebUI versions report a per-request failure in the body with a 200 status.
            if (body.Equals(FailedBody, StringComparison.OrdinalIgnoreCase))
            {
                throw new QbittorrentException("qBittorrent failed to add the torrent.");
            }

            _logger.LogInformation(
                "Added torrent {FileName} to qBittorrent with save path {SavePath}",
                fileName,
                savePath);
        }
    }

    private async Task<HttpResponseMessage> SendAddTorrentAsync(
        HttpClient client,
        Uri baseUrl,
        string fileName,
        byte[] content,
        string savePath,
        CancellationToken cancellationToken)
    {
        await EnsureLoggedInAsync(client, baseUrl, cancellationToken);

        using var form = BuildAddTorrentForm(fileName, content, savePath);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUrl, AddTorrentPath))
        {
            Content = form
        };
        request.Headers.Referrer = baseUrl;

        if (!string.IsNullOrWhiteSpace(_sessionCookie))
        {
            request.Headers.Add("Cookie", _sessionCookie);
        }

        return await SendAsync(client, request, baseUrl, cancellationToken);
    }

    private MultipartFormDataContent BuildAddTorrentForm(string fileName, byte[] content, string savePath)
    {
        var form = new MultipartFormDataContent();

        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(TorrentMimeType);
        form.Add(fileContent, "torrents", fileName);

        form.Add(new StringContent(savePath), "savepath");

        // Add the torrent in the running state so the download starts immediately.
        form.Add(new StringContent("false"), "paused");

        if (!string.IsNullOrWhiteSpace(_options.Category))
        {
            form.Add(new StringContent(_options.Category), "category");
        }

        if (!string.IsNullOrWhiteSpace(_options.Tags))
        {
            form.Add(new StringContent(_options.Tags), "tags");
        }

        return form;
    }

    private async Task EnsureLoggedInAsync(HttpClient client, Uri baseUrl, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_sessionCookie))
        {
            return;
        }

        await _loginLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(_sessionCookie))
            {
                return;
            }

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = _options.Username ?? string.Empty,
                ["password"] = _options.Password ?? string.Empty
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUrl, LoginPath))
            {
                Content = content
            };
            request.Headers.Referrer = baseUrl;

            using var response = await SendAsync(client, request, baseUrl, cancellationToken);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new QbittorrentException(
                    "qBittorrent login failed. Check the configured username and password.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new QbittorrentException(
                    $"qBittorrent login returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            var body = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();

            // qBittorrent 5.x replies 204 with an empty body; 4.x replies 200 with "Ok.".
            if (body.Equals(FailedBody, StringComparison.OrdinalIgnoreCase))
            {
                throw new QbittorrentException(
                    "qBittorrent login failed. Check the configured username and password.");
            }

            var sessionCookie = ReadSessionCookie(response);
            if (string.IsNullOrWhiteSpace(sessionCookie))
            {
                throw new QbittorrentException("qBittorrent login succeeded but no session cookie was returned.");
            }

            _sessionCookie = sessionCookie;
            _logger.LogInformation("Authenticated with qBittorrent at {BaseUrl}", baseUrl);
        }
        finally
        {
            _loginLock.Release();
        }
    }

    /// <summary>
    /// Extracts the session cookie as a ready-to-send <c>name=value</c> pair.
    /// qBittorrent 4.x uses <c>SID</c>; 5.x uses <c>QBT_SID_&lt;port&gt;</c>.
    /// </summary>
    private static string? ReadSessionCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return null;
        }

        foreach (var cookie in cookies)
        {
            var pair = cookie.Split(';', 2)[0].Trim();
            var separator = pair.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var name = pair[..separator];
            if (name.Equals("SID", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("QBT_SID", StringComparison.OrdinalIgnoreCase))
            {
                return pair;
            }
        }

        return null;
    }

    private Uri GetBaseUrl()
    {
        var url = _options.Url;
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new QbittorrentNotConfiguredException(
                "qBittorrent is not configured. Set MediaOrganizer:Qbittorrent:Url in appsettings.");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var baseUrl))
        {
            throw new QbittorrentNotConfiguredException(
                $"MediaOrganizer:Qbittorrent:Url is not a valid absolute URL: {url}");
        }

        return baseUrl;
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient(nameof(QbittorrentClient));
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.RequestTimeoutSeconds, 5, 600));
        return client;
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpRequestMessage request,
        Uri baseUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            return await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new QbittorrentException($"Could not reach qBittorrent at {baseUrl}: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new QbittorrentException($"The request to qBittorrent at {baseUrl} timed out.", ex);
        }
    }
}
