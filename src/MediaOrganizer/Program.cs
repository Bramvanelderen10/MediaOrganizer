using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MediaOrganizer.Cleanup;
using MediaOrganizer.Configuration;
using MediaOrganizer.Discovery;
using MediaOrganizer.Endpoints;
using MediaOrganizer.Execution;
using MediaOrganizer.Helpers;
using MediaOrganizer.History;
using MediaOrganizer.Logging;
using MediaOrganizer.Orchestration;
using MediaOrganizer.Parsing;
using MediaOrganizer.Planning;
using MediaOrganizer.Torrents;
using MediaOrganizer.Transcoding;

using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to listen on port 45263
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(45263);
});

// Add background service
builder.Services.AddSingleton<JobExecutor>();
builder.Services.AddSingleton<JobLock>();
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

// Transcoding: optional ffmpeg step that converts unsupported codecs (e.g. HEVC) to a
// hardware-friendly one (H.264 via VA-API/Quick Sync, with a libx264 software fallback).
builder.Services.AddSingleton<IProcessRunner, PhysicalProcessRunner>();
builder.Services.AddSingleton<IVideoProbe, FfprobeVideoProbe>();
builder.Services.AddSingleton<ITranscoder, FfmpegTranscoder>();
builder.Services.AddSingleton<TranscodeService>();

// Torrents: accepts .torrent uploads and hands them to qBittorrent to download.
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ITorrentClient, QbittorrentClient>();
builder.Services.AddSingleton<TorrentService>();

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

app.MapJobEndpoints();
app.MapTranscodeEndpoints();
app.MapHistoryEndpoints();
app.MapFileManagementEndpoints();
app.MapSystemEndpoints();
app.MapTorrentEndpoints();

app.Run();
