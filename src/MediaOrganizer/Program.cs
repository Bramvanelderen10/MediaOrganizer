using Microsoft.EntityFrameworkCore;
using MediaOrganizer;
using Scalar.AspNetCore;
using MediaOrganizer.MovePlan;
using MediaOrganizer.MoveHistory;
using MediaOrganizer.MediaGrouper;

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
builder.Services.AddSingleton<VideoFileFinder>();
builder.Services.AddSingleton<MediaGrouper>();
builder.Services.AddSingleton<MovePlanBuilder>();

var dbPath = MoveHistoryStore.ResolveDatabasePath(
    builder.Configuration.GetValue<string>("MediaOrganizer:MoveHistoryDatabasePath") ?? "");
builder.Services.AddDbContextFactory<MoveHistoryDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddSingleton<MoveHistoryStore>();
builder.Services.AddSingleton<MediaFileOrganizer>();
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

app.MapPost("/restore-folder-structure", async (MediaFileOrganizer mediaFileOrganizer) =>
{
    var summary = await mediaFileOrganizer.RestoreAllAsync();
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

app.MapGet("/", () => Results.Ok(new
{
    message = "Scheduled Job Application",
    endpoints = new
    {
        triggerJob = "POST /trigger-job",
        restoreFolderStructure = "POST /restore-folder-structure",
        health = "GET /health",
        openApiSpec = "GET /openapi/v1.json",
        apiReference = "GET /scalar/v1"
    },
    schedule = "Daily at 5:00 AM"
}))
.WithName("Root")
.WithSummary("Returns API overview");

app.Run();

public record TriggerJobRequest(string? FolderPath);
