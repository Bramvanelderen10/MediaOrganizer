using MediaOrganizer.Orchestration;
using MediaOrganizer.Transcoding;

namespace MediaOrganizer.Endpoints;

public static class TranscodeEndpoints
{
    public static void MapTranscodeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/transcode", async (
            TranscodeService transcodeService,
            TranscodeRequest? request,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var summary = await transcodeService.TranscodeRequestAsync(request?.Paths, cancellationToken);

                return Results.Ok(new
                {
                    message = "Transcode run completed",
                    totalFiles = summary.TotalFiles,
                    transcodedFiles = summary.TranscodedFiles,
                    skippedFiles = summary.SkippedFiles,
                    failedFiles = summary.FailedFiles,
                    executedAt = DateTime.Now
                });
            }
            catch (Exception ex) when (IsHandled(ex))
            {
                return ToErrorResult(ex);
            }
        })
        .WithName("Transcode")
        .WithSummary("Transcodes media files that are not yet in the target codec")
        .WithDescription("Without a body, scans the media library and re-encodes every video file whose codec is not Transcoding:TargetCodec. Pass {\"paths\": [\"...\"]} to transcode only those files (used by the library screen buttons). Files whose codec is not in Transcoding:OnlyCodecs are skipped when that list is non-empty. Requires MediaOrganizer:Transcoding:Enabled=true.");

        app.MapGet("/transcode/status", async (
            TranscodeService transcodeService,
            CancellationToken cancellationToken) =>
        {
            var status = await transcodeService.GetStatusAsync(cancellationToken);

            return Results.Ok(new
            {
                enabled = status.Enabled,
                encoder = status.Encoder,
                hardwareDevice = status.HardwareDevice,
                hardwareAvailable = status.HardwareAvailable
            });
        })
        .WithName("TranscodeStatus")
        .WithSummary("Reports transcoding configuration and hardware availability");
    }

    /// <summary>
    /// Maps known transcode failures onto HTTP status codes. Unknown exceptions bubble up as a 500.
    /// </summary>
    private static bool IsHandled(Exception ex)
        => ex is TranscodeException or TranscodeValidationException or JobAlreadyRunningException;

    private static IResult ToErrorResult(Exception ex) => ex switch
    {
        TranscodeValidationException => Results.BadRequest(new { message = ex.Message }),
        TranscodeNotConfiguredException => Results.Json(
            new { message = ex.Message }, statusCode: StatusCodes.Status503ServiceUnavailable),
        JobAlreadyRunningException => Results.Json(
            new { message = ex.Message }, statusCode: StatusCodes.Status409Conflict),
        _ => Results.Json(
            new { message = ex.Message }, statusCode: StatusCodes.Status502BadGateway)
    };
}