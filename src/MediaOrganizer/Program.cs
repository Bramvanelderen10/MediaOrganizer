using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MediaOrganizer.Cleanup;
using MediaOrganizer.Configuration;
using MediaOrganizer.Discovery;
using MediaOrganizer.Execution;
using MediaOrganizer.Helpers;
using MediaOrganizer.History;
using MediaOrganizer.Logging;
using MediaOrganizer.Orchestration;
using MediaOrganizer.Parsing;
using MediaOrganizer.Planning;

using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to listen on port 45263
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(45263);
});

// Add background service
builder.Services.AddHostedService<ScheduledJobService>();
builder.Services.AddSingleton<JobExecutor>();
builder.Services.Configure<MediaOrganizerOptions>(builder.Configuration.GetSection("MediaOrganizer"));
builder.Services.AddSingleton<IFileSystem, PhysicalFileSystem>();
builder.Services.AddSingleton<VideoFileFinder>();
builder.Services.AddSingleton<MediaGrouper>();
builder.Services.AddSingleton<MovePlanBuilder>();
builder.Services.AddSingleton<VideoMover>();
builder.Services.AddSingleton<SubtitleMover>();
builder.Services.AddSingleton<DirectoryCleaner>();

var dbPath = MoveHistoryStore.ResolveDatabasePath(
    builder.Configuration.GetValue<string>("MediaOrganizer:MoveHistoryDatabasePath") ?? "");
builder.Services.AddDbContextFactory<MoveHistoryDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddSingleton<MoveHistoryStore>();
builder.Services.AddSingleton<MediaFileOrganizer>();
builder.Services.AddSingleton<MediaFileRestorer>();

// Live log streaming (SSE)
builder.Services.AddSingleton<LogBroadcaster>();
builder.Services.AddSingleton<ILoggerProvider, BroadcastLoggerProvider>();
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options.EndpointPathPrefix = "/scalar/{documentName}";
});
app.MapGet("/scalar", () => Results.Redirect("/scalar/v1", permanent: false));

// HTTP trigger endpoint
app.MapPost("/trigger-job", async (JobExecutor jobExecutor, TriggerJobRequest? request) =>
{
    var result = await jobExecutor.ExecuteJobAsync(request?.FolderPath);
    return Results.Ok(new
    {
        message = "Job triggered successfully",
        executedAt = DateTime.Now,
        result = result
    });
})
.WithName("TriggerJob")
.WithSummary("Triggers the media organization job immediately")
.WithDescription("Optionally accepts a custom folder path. If omitted, configured defaults are used.");

app.MapPost("/restore-folder-structure", async (MediaFileRestorer mediaFileRestorer) =>
{
    var summary = await mediaFileRestorer.RestoreAllAsync();
    return Results.Ok(new
    {
        message = "Restore completed",
        executedAt = DateTime.Now,
        result = summary
    });
})
.WithName("RestoreFolderStructure")
.WithSummary("Restores all tracked file moves back to their original structure")
.WithDescription("Reverts all files tracked in the move-history database that have not yet been restored.");

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.Now
}))
.WithName("Health")
.WithSummary("Returns service health");

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
        // SSE requires each line to be prefixed with 'data: '
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

app.MapGet("/", () => Results.Ok(new
{
    message = "Scheduled Job Application",
    endpoints = new
    {
        triggerJob = "POST /trigger-job",
        restoreFolderStructure = "POST /restore-folder-structure",
        health = "GET /health",
        streamLogs = "GET /logs/stream",
        openApiSpec = "GET /openapi/v1.json",
        apiReference = "GET /scalar/v1"
    },
    schedule = "Daily at 5:00 AM"
}))
.WithName("Root")
.WithSummary("Returns API overview");

app.Run();

public record TriggerJobRequest(string? FolderPath);
