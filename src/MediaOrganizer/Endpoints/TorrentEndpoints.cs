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
            catch (TorrentValidationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
            catch (QbittorrentNotConfiguredException ex)
            {
                return Results.Json(
                    new { message = ex.Message },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (QbittorrentException ex)
            {
                return Results.Json(
                    new { message = ex.Message },
                    statusCode: StatusCodes.Status502BadGateway);
            }
        })
        .DisableAntiforgery()
        .Accepts<IFormFile>("multipart/form-data")
        .WithName("AddTorrent")
        .WithSummary("Adds a .torrent file to qBittorrent and starts the download")
        .WithDescription("Accepts a multipart/form-data upload with a .torrent file in the 'file' field. Optional 'folderPath' form field overrides the configured download folder. The organize job is not triggered; downloads are organized the next time /trigger-job runs.");
    }
}
