using MediaOrganizer.Orchestration;
using MediaOrganizer.Transcoding;

namespace MediaOrganizer.Endpoints;

public static class TranscodeEndpoints
{
    public static void MapTranscodeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/transcode", (
            TranscodeJobRunner jobRunner,
            TranscodeRequest? request) =>
        {
            try
            {
                var status = jobRunner.Start(request?.Label, request?.Paths);

                return Results.Json(new
                {
                    message = "Transcode job started",
                    started = true,
                    label = status.Label,
                    totalFiles = status.TotalFiles,
                    startedAt = status.StartedAt
                }, statusCode: StatusCodes.Status202Accepted);
            }
            catch (Exception ex) when (IsHandled(ex))
            {
                return ToErrorResult(ex);
            }
        })
        .WithName("Transcode")
        .WithSummary("Starts a transcode job in the background and returns immediately")
        .WithDescription("Returns 202 Accepted right away. Omit the body to transcode the whole library, or pass {\"paths\": [\"...\"], \"label\": \"...\"} to transcode specific files (used by the library screen buttons). Poll GET /transcode/job for progress. Requires MediaOrganizer:Transcoding:Enabled=true.");

        app.MapGet("/transcode/job", (TranscodeJobRunner jobRunner) =>
        {
            var status = jobRunner.GetStatus();

            return Results.Ok(new
            {
                state = status.State.ToString().ToLowerInvariant(),
                isRunning = status.IsRunning,
                label = status.Label,
                startedAt = status.StartedAt,
                finishedAt = status.FinishedAt,
                totalFiles = status.TotalFiles,
                processedFiles = status.ProcessedFiles,
                transcodedFiles = status.TranscodedFiles,
                skippedFiles = status.SkippedFiles,
                failedFiles = status.FailedFiles,
                currentFile = status.CurrentFile,
                error = status.Error
            });
        })
        .WithName("TranscodeJob")
        .WithSummary("Reports the current or most recent transcode job")
        .WithDescription("Poll this to follow a background transcode: state, progress counters and the file currently being encoded.");

        app.MapGet("/transcode/selftest", async (
            TranscodeService transcodeService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await transcodeService.RunSelfTestAsync(cancellationToken);

                return Results.Ok(new
                {
                    enabled = result.Enabled,
                    encoder = result.Encoder,
                    hardwareDevice = result.HardwareDevice,
                    isHardwareEncoder = result.IsHardwareEncoder,
                    encoderAvailable = result.EncoderAvailable,
                    encodeSucceeded = result.EncodeSucceeded,
                    hardwareEncode = result.HardwareEncode,
                    driver = result.Driver,
                    output = result.Output,
                    hint = result.Hint
                });
            }
            catch (Exception ex) when (IsHandled(ex))
            {
                return ToErrorResult(ex);
            }
        })
        .WithName("TranscodeSelfTest")
        .WithSummary("Runs a short real encode to verify hardware acceleration")
        .WithDescription("Runs a 2 second synthetic encode with the configured encoder and reports whether hardware encoding actually worked, plus which VA-API driver loaded. Unlike GET /transcode/status, this exercises the GPU/driver for real.");

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