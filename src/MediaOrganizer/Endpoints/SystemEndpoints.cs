using MediaOrganizer.Configuration;
using MediaOrganizer.Logging;

namespace MediaOrganizer.Endpoints;

public static class SystemEndpoints
{
    public static void MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/", () => Results.Ok(new
        {
            message = "Media Organizer API",
            endpoints = new
            {
                triggerJob = "POST /trigger-job",
                forgetMovie = "POST /forget-movie",
                forgetShow = "POST /forget-show",
                forgetShowSeason = "POST /forget-show-season",
                forgetEpisode = "POST /forget-episode",
                forgetBatch = "POST /forget-batch",
                browse = "GET /browse",
                rename = "POST /rename",
                moveItem = "POST /move",
                delete = "POST /delete",
                storageInfo = "GET /storage-info",
                library = "GET /library",
                health = "GET /health",
                streamLogs = "GET /logs/stream",
                openApiSpec = "GET /openapi/v1.json",
                apiReference = "GET /scalar/v1"
            },
            schedule = "Daily at 5:00 AM"
        }))
        .WithName("Root")
        .WithSummary("Returns API overview");

        app.MapGet("/health", () => Results.Ok(new
        {
            status = "healthy",
            timestamp = DateTime.Now
        }))
        .WithName("Health")
        .WithSummary("Returns service health");

        app.MapGet("/storage-info", (Microsoft.Extensions.Options.IOptions<MediaOrganizerOptions> options) =>
        {
            var opts = options.Value;
            var folder = opts.DestinationFolder ?? opts.SourceFolder;

            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                return Results.BadRequest(new
                {
                    message = "No valid destination/source folder configured or folder does not exist."
                });
            }

            var fullPath = Path.GetFullPath(folder);
            var driveInfo = DriveInfo.GetDrives()
                .Where(d => d.IsReady && fullPath.StartsWith(d.RootDirectory.FullName, StringComparison.Ordinal))
                .OrderByDescending(d => d.RootDirectory.FullName.Length)
                .First();

            return Results.Ok(new
            {
                folder,
                totalBytes = driveInfo.TotalSize,
                freeBytes = driveInfo.AvailableFreeSpace,
                usedBytes = driveInfo.TotalSize - driveInfo.AvailableFreeSpace
            });
        })
        .WithName("StorageInfo")
        .WithSummary("Returns disk storage information for the media destination folder")
        .WithDescription("Reports total, used, and free bytes for the drive containing the configured destination (or source) folder.");

        app.MapGet("/logs/stream", async (HttpContext context, LogBroadcaster broadcaster, int? tail) =>
        {
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";
            context.Response.Headers["X-Accel-Buffering"] = "no";
            context.Response.ContentType = "text/event-stream";

            static string Format(LogEvent evt)
            {
                var level = evt.Level.ToString().ToUpperInvariant();
                return $"{evt.Timestamp:O} [{level}] {evt.Category}: {evt.Message}";
            }

            static async Task WriteSseAsync(HttpContext ctx, string data, CancellationToken ct)
            {
                data = data.Replace("\r\n", "\n").Replace("\r", "\n");
                foreach (var line in data.Split('\n'))
                {
                    await ctx.Response.WriteAsync($"data: {line}\n", ct);
                }
                await ctx.Response.WriteAsync("\n", ct);
                await ctx.Response.Body.FlushAsync(ct);
            }

            var tailCount = Math.Clamp(tail ?? 200, 0, 1000);
            foreach (var evt in broadcaster.GetRecent(tailCount))
            {
                await WriteSseAsync(context, Format(evt), context.RequestAborted);
            }

            var subscription = broadcaster.Subscribe(bufferCapacity: 512);
            try
            {
                await foreach (var evt in subscription.Reader.ReadAllAsync(context.RequestAborted))
                {
                    await WriteSseAsync(context, Format(evt), context.RequestAborted);
                }
            }
            finally
            {
                subscription.Unsubscribe();
            }
        })
        .WithName("StreamLogs")
        .WithSummary("Streams application logs as Server-Sent Events (SSE)")
        .WithDescription("Client connects and receives live log lines. Optional query parameter: ?tail=200");
    }
}
