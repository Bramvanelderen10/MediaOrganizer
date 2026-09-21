using MediaOrganizer.Torrents;

using Microsoft.AspNetCore.Mvc;

namespace MediaOrganizer.Endpoints;

public static class TorrentEndpoints
{
    public static void MapTorrentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/torrents/add", async (
            TorrentService torrentService,
            IFormFile? file,
            [FromForm] string? folderPath,
            CancellationToken cancellationToken) =>
        {
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { message = "A non-empty .torrent file is required." });
            }

            byte[] content;
            using (var buffer = new MemoryStream())
            {
                await file.CopyToAsync(buffer, cancellationToken);
                content = buffer.ToArray();
            }

            try
            {
                var result = await torrentService.AddTorrentAsync(
                    file.FileName,
                    content,
                    folderPath,
                    cancellationToken);

                return Results.Ok(new
                {
                    message = "Torrent added and download started",
                    fileName = result.FileName,
                    savePath = result.SavePath,
                    sizeBytes = result.SizeBytes,
                    executedAt = DateTime.Now
                });
            }
            catch (Exception ex) when (IsHandled(ex))
            {
                return ToErrorResult(ex);
            }
        })
        .DisableAntiforgery()
        .Accepts<IFormFile>("multipart/form-data")
        .WithName("AddTorrent")
        .WithSummary("Adds a .torrent file to qBittorrent and starts the download")
        .WithDescription("Accepts a multipart/form-data upload with a .torrent file in the 'file' field. Optional 'folderPath' form field overrides the configured download folder. The organize job is not triggered; downloads are organized the next time /trigger-job runs.");

        app.MapPost("/torrents/add-magnet", async (
            TorrentService torrentService,
            AddMagnetRequest request,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await torrentService.AddMagnetAsync(
                    request.MagnetLink,
                    request.FolderPath,
                    cancellationToken);

                return Results.Ok(new
                {
                    message = "Magnet link added and download started",
                    name = result.FileName,
                    savePath = result.SavePath,
                    executedAt = DateTime.Now
                });
            }
            catch (Exception ex) when (IsHandled(ex))
            {
                return ToErrorResult(ex);
            }
        })
        .WithName("AddMagnet")
        .WithSummary("Adds a magnet link to qBittorrent and starts the download")
        .WithDescription("Accepts JSON: { \"magnetLink\": \"magnet:?xt=urn:btih:...\", \"folderPath\": \"/media\" }. The folderPath is optional and overrides the configured download folder. The organize job is not triggered.");

        app.MapGet("/torrents", async (
            TorrentService torrentService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var torrents = await torrentService.GetTorrentsAsync(cancellationToken);

                return Results.Ok(new
                {
                    count = torrents.Count,
                    torrents = torrents.Select(t => new
                    {
                        hash = t.Hash,
                        name = t.Name,
                        state = t.State,
                        status = t.Status,
                        progress = t.Progress,
                        sizeBytes = t.SizeBytes,
                        downloadedBytes = t.DownloadedBytes,
                        amountLeftBytes = t.AmountLeftBytes,
                        downloadSpeed = t.DownloadSpeed,
                        uploadSpeed = t.UploadSpeed,
                        etaSeconds = t.EtaSeconds,
                        savePath = t.SavePath,
                        addedOnUnixSeconds = t.AddedOnUnixSeconds
                    })
                });
            }
            catch (Exception ex) when (IsHandled(ex))
            {
                return ToErrorResult(ex);
            }
        })
        .WithName("GetTorrents")
        .WithSummary("Lists torrents with their download progress")
        .WithDescription("Returns every torrent known to qBittorrent with progress, speed, ETA and state information.");
    }

    /// <summary>
    /// Maps known torrent failures onto HTTP status codes. Unknown exceptions are left
    /// to bubble up so they surface as a 500.
    /// </summary>
    private static bool IsHandled(Exception ex)
        => ex is TorrentValidationException or QbittorrentException;

    private static IResult ToErrorResult(Exception ex) => ex switch
    {
        TorrentValidationException => Results.BadRequest(new { message = ex.Message }),
        QbittorrentNotConfiguredException => Results.Json(
            new { message = ex.Message }, statusCode: StatusCodes.Status503ServiceUnavailable),
        _ => Results.Json(
            new { message = ex.Message }, statusCode: StatusCodes.Status502BadGateway)
    };
}
