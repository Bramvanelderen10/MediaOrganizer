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

using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to listen on port 45263
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(45263);
});

// Add background service
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
app.MapHistoryEndpoints();
app.MapFileManagementEndpoints();
app.MapSystemEndpoints();

app.Run();
