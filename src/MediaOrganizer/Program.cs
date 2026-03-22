using System.Text.RegularExpressions;

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

app.MapPost("/forget-show-season", (MoveHistoryStore moveHistoryStore, ForgetShowSeasonRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.ShowName))
    {
        return Results.BadRequest(new
        {
            message = "showName is required"
        });
    }

    if (request.SeasonNumber <= 0)
    {
        return Results.BadRequest(new
        {
            message = "seasonNumber must be greater than 0"
        });
    }

    var deletedCount = moveHistoryStore.ForgetShowSeason(request.ShowName, request.SeasonNumber);

    return Results.Ok(new
    {
        message = "Forget season completed",
        executedAt = DateTime.Now,
        showName = request.ShowName,
        seasonNumber = request.SeasonNumber,
        deletedCount
    });
})
.WithName("ForgetShowSeason")
.WithSummary("Deletes move-history entries for a specific show season")
.WithDescription("Removes all tracked move-history rows for the given show name and season number.");

app.MapPost("/forget-movie", (MoveHistoryStore moveHistoryStore, ForgetMovieRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.MovieName))
    {
        return Results.BadRequest(new { message = "movieName is required" });
    }

    var deletedCount = moveHistoryStore.ForgetMovie(request.MovieName);

    return Results.Ok(new
    {
        message = "Forget movie completed",
        executedAt = DateTime.Now,
        movieName = request.MovieName,
        deletedCount
    });
})
.WithName("ForgetMovie")
.WithSummary("Deletes move-history entries for a specific movie")
.WithDescription("Removes tracked move-history rows matching the given movie name.");

app.MapPost("/forget-show", (MoveHistoryStore moveHistoryStore, ForgetShowRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.ShowName))
    {
        return Results.BadRequest(new { message = "showName is required" });
    }

    var deletedCount = moveHistoryStore.ForgetShow(request.ShowName);

    return Results.Ok(new
    {
        message = "Forget show completed",
        executedAt = DateTime.Now,
        showName = request.ShowName,
        deletedCount
    });
})
.WithName("ForgetShow")
.WithSummary("Deletes all move-history entries for a show")
.WithDescription("Removes all tracked move-history rows for the given show name across all seasons.");

app.MapPost("/forget-episode", (MoveHistoryStore moveHistoryStore, ForgetEpisodeRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.ShowName))
    {
        return Results.BadRequest(new { message = "showName is required" });
    }

    if (request.SeasonNumber <= 0)
    {
        return Results.BadRequest(new { message = "seasonNumber must be greater than 0" });
    }

    if (request.EpisodeNumber <= 0)
    {
        return Results.BadRequest(new { message = "episodeNumber must be greater than 0" });
    }

    var deletedCount = moveHistoryStore.ForgetEpisode(request.ShowName, request.SeasonNumber, request.EpisodeNumber);

    return Results.Ok(new
    {
        message = "Forget episode completed",
        executedAt = DateTime.Now,
        showName = request.ShowName,
        seasonNumber = request.SeasonNumber,
        episodeNumber = request.EpisodeNumber,
        deletedCount
    });
})
.WithName("ForgetEpisode")
.WithSummary("Deletes the move-history entry for a specific episode")
.WithDescription("Removes the tracked move-history row for the given show name, season, and episode number.");

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

app.MapGet("/library", (MoveHistoryStore moveHistoryStore) =>
{
    var entries = moveHistoryStore.GetMovedEntries();

    var movies = new List<object>();
    var shows = new Dictionary<string, Dictionary<int, List<object>>>();

    var seasonEpisodePattern = new Regex(@"^(.+)_Season(\d+)_Episode(\d+)$", RegexOptions.None, TimeSpan.FromSeconds(1));

    foreach (var entry in entries)
    {
        var match = seasonEpisodePattern.Match(entry.UniqueKey);
        if (match.Success)
        {
            var showName = match.Groups[1].Value;
            var season = int.Parse(match.Groups[2].Value);
            var episode = int.Parse(match.Groups[3].Value);

            if (!shows.ContainsKey(showName))
                shows[showName] = new Dictionary<int, List<object>>();

            if (!shows[showName].ContainsKey(season))
                shows[showName][season] = new List<object>();

            shows[showName][season].Add(new
            {
                episodeNumber = episode,
                originalPath = entry.OriginalFilePath,
                targetPath = entry.TargetFilePath
            });
        }
        else
        {
            movies.Add(new
            {
                name = entry.UniqueKey,
                originalPath = entry.OriginalFilePath,
                targetPath = entry.TargetFilePath
            });
        }
    }

    var showsList = shows.Select(s => new
    {
        name = s.Key,
        seasons = s.Value
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => new
            {
                seasonNumber = kvp.Key,
                episodes = kvp.Value.OrderBy(e => ((dynamic)e).episodeNumber).ToList()
            })
            .ToList()
    })
    .OrderBy(s => s.name)
    .ToList();

    return Results.Ok(new
    {
        movies = movies.OrderBy(m => ((dynamic)m).name).ToList(),
        shows = showsList
    });
})
.WithName("Library")
.WithSummary("Returns the organized media library structure")
.WithDescription("Parses move history unique keys to build a structured view of movies and TV shows with seasons and episodes.");

app.MapPost("/forget-batch", (MoveHistoryStore moveHistoryStore, ForgetBatchRequest request) =>
{
    if (request.Items is null || request.Items.Length == 0)
    {
        return Results.BadRequest(new { message = "items array is required and must not be empty" });
    }

    var totalDeleted = 0;
    foreach (var item in request.Items)
    {
        totalDeleted += item.Type?.ToLowerInvariant() switch
        {
            "movie" => moveHistoryStore.ForgetMovie(item.MovieName ?? ""),
            "show" => moveHistoryStore.ForgetShow(item.ShowName ?? ""),
            "season" => moveHistoryStore.ForgetShowSeason(item.ShowName ?? "", item.SeasonNumber ?? 0),
            "episode" => moveHistoryStore.ForgetEpisode(item.ShowName ?? "", item.SeasonNumber ?? 0, item.EpisodeNumber ?? 0),
            _ => 0
        };
    }

    return Results.Ok(new
    {
        message = "Batch forget completed",
        executedAt = DateTime.Now,
        itemCount = request.Items.Length,
        deletedCount = totalDeleted
    });
})
.WithName("ForgetBatch")
.WithSummary("Deletes move-history entries for multiple items at once")
.WithDescription("Accepts an array of items, each specifying a type (movie, show, season, episode) and the relevant identifiers.");

app.MapGet("/browse", (IFileSystem fileSystem, Microsoft.Extensions.Options.IOptions<MediaOrganizerOptions> options, string? path) =>
{
    var opts = options.Value;
    var root = opts.SourceFolder;
    if (string.IsNullOrWhiteSpace(root) || !fileSystem.DirectoryExists(root))
    {
        return Results.BadRequest(new { message = "Source folder is not configured or does not exist." });
    }

    root = Path.GetFullPath(root);
    var target = string.IsNullOrWhiteSpace(path) ? root : Path.GetFullPath(Path.Combine(root, path));

    if (!target.StartsWith(root, StringComparison.Ordinal))
    {
        return Results.BadRequest(new { message = "Path is outside the source folder." });
    }

    if (!fileSystem.DirectoryExists(target))
    {
        return Results.NotFound(new { message = "Directory not found." });
    }

    var directories = fileSystem.EnumerateDirectories(target)
        .Select(d => new { name = Path.GetFileName(d), path = Path.GetRelativePath(root, d) })
        .OrderBy(d => d.name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    var files = fileSystem.EnumerateFiles(target, "*", SearchOption.TopDirectoryOnly)
        .Select(f => new
        {
            name = Path.GetFileName(f),
            path = Path.GetRelativePath(root, f),
            size = new FileInfo(f).Length
        })
        .OrderBy(f => f.name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    return Results.Ok(new
    {
        currentPath = Path.GetRelativePath(root, target),
        directories,
        files
    });
})
.WithName("Browse")
.WithSummary("Lists directory contents under the source folder")
.WithDescription("Optional query parameter: ?path=sub/dir relative to the source root. Returns folders and files.");

app.MapPost("/rename", (IFileSystem fileSystem, Microsoft.Extensions.Options.IOptions<MediaOrganizerOptions> options, RenameRequest request) =>
{
    var opts = options.Value;
    var root = opts.SourceFolder;
    if (string.IsNullOrWhiteSpace(root) || !fileSystem.DirectoryExists(root))
    {
        return Results.BadRequest(new { message = "Source folder is not configured or does not exist." });
    }

    if (string.IsNullOrWhiteSpace(request.Path) || string.IsNullOrWhiteSpace(request.NewName))
    {
        return Results.BadRequest(new { message = "path and newName are required." });
    }

    if (request.NewName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
    {
        return Results.BadRequest(new { message = "newName contains invalid characters." });
    }

    root = Path.GetFullPath(root);
    var fullPath = Path.GetFullPath(Path.Combine(root, request.Path));

    if (!fullPath.StartsWith(root, StringComparison.Ordinal))
    {
        return Results.BadRequest(new { message = "Path is outside the source folder." });
    }

    var parentDir = Path.GetDirectoryName(fullPath)!;
    var newFullPath = Path.Combine(parentDir, request.NewName);

    if (!newFullPath.StartsWith(root, StringComparison.Ordinal))
    {
        return Results.BadRequest(new { message = "New path would be outside the source folder." });
    }

    if (fileSystem.FileExists(fullPath))
    {
        if (fileSystem.FileExists(newFullPath))
        {
            return Results.Conflict(new { message = "A file with that name already exists." });
        }
        fileSystem.MoveFile(fullPath, newFullPath);
    }
    else if (fileSystem.DirectoryExists(fullPath))
    {
        if (fileSystem.DirectoryExists(newFullPath))
        {
            return Results.Conflict(new { message = "A directory with that name already exists." });
        }
        Directory.Move(fullPath, newFullPath);
    }
    else
    {
        return Results.NotFound(new { message = "File or directory not found." });
    }

    return Results.Ok(new
    {
        message = "Renamed successfully",
        oldPath = request.Path,
        newPath = Path.GetRelativePath(root, newFullPath)
    });
})
.WithName("Rename")
.WithSummary("Renames a file or directory under the source folder");

app.MapPost("/move", (IFileSystem fileSystem, Microsoft.Extensions.Options.IOptions<MediaOrganizerOptions> options, MoveItemRequest request) =>
{
    var opts = options.Value;
    var root = opts.SourceFolder;
    if (string.IsNullOrWhiteSpace(root) || !fileSystem.DirectoryExists(root))
    {
        return Results.BadRequest(new { message = "Source folder is not configured or does not exist." });
    }

    if (string.IsNullOrWhiteSpace(request.SourcePath) || string.IsNullOrWhiteSpace(request.DestinationFolder))
    {
        return Results.BadRequest(new { message = "sourcePath and destinationFolder are required." });
    }

    root = Path.GetFullPath(root);
    var srcFull = Path.GetFullPath(Path.Combine(root, request.SourcePath));
    var destDir = Path.GetFullPath(Path.Combine(root, request.DestinationFolder));

    if (!srcFull.StartsWith(root, StringComparison.Ordinal) || !destDir.StartsWith(root, StringComparison.Ordinal))
    {
        return Results.BadRequest(new { message = "Paths must be within the source folder." });
    }

    if (!fileSystem.DirectoryExists(destDir))
    {
        return Results.NotFound(new { message = "Destination directory not found." });
    }

    var fileName = Path.GetFileName(srcFull);
    var destFull = Path.Combine(destDir, fileName);

    if (fileSystem.FileExists(srcFull))
    {
        if (fileSystem.FileExists(destFull))
        {
            return Results.Conflict(new { message = "A file with that name already exists in the destination." });
        }
        fileSystem.MoveFile(srcFull, destFull);
    }
    else if (fileSystem.DirectoryExists(srcFull))
    {
        if (fileSystem.DirectoryExists(destFull))
        {
            return Results.Conflict(new { message = "A directory with that name already exists in the destination." });
        }
        Directory.Move(srcFull, destFull);
    }
    else
    {
        return Results.NotFound(new { message = "Source file or directory not found." });
    }

    return Results.Ok(new
    {
        message = "Moved successfully",
        sourcePath = request.SourcePath,
        newPath = Path.GetRelativePath(root, destFull)
    });
})
.WithName("MoveItem")
.WithSummary("Moves a file or directory to a different folder under the source root");

app.MapPost("/delete", (IFileSystem fileSystem, Microsoft.Extensions.Options.IOptions<MediaOrganizerOptions> options, DeleteRequest request) =>
{
    var opts = options.Value;
    var root = opts.SourceFolder;
    if (string.IsNullOrWhiteSpace(root) || !fileSystem.DirectoryExists(root))
    {
        return Results.BadRequest(new { message = "Source folder is not configured or does not exist." });
    }

    if (request.Paths is null || request.Paths.Length == 0)
    {
        return Results.BadRequest(new { message = "paths array is required and must not be empty." });
    }

    root = Path.GetFullPath(root);
    var deleted = 0;
    var errors = new List<string>();

    foreach (var p in request.Paths)
    {
        if (string.IsNullOrWhiteSpace(p))
        {
            errors.Add("Empty path skipped.");
            continue;
        }

        var fullPath = Path.GetFullPath(Path.Combine(root, p));

        if (!fullPath.StartsWith(root, StringComparison.Ordinal) || fullPath == root)
        {
            errors.Add($"{p}: cannot delete (outside source or is source root).");
            continue;
        }

        try
        {
            if (fileSystem.FileExists(fullPath))
            {
                fileSystem.DeleteFile(fullPath);
                deleted++;
            }
            else if (fileSystem.DirectoryExists(fullPath))
            {
                fileSystem.DeleteDirectory(fullPath, recursive: true);
                deleted++;
            }
            else
            {
                errors.Add($"{p}: not found.");
            }
        }
        catch (Exception ex)
        {
            errors.Add($"{p}: {ex.Message}");
        }
    }

    return Results.Ok(new
    {
        message = "Delete completed",
        deletedCount = deleted,
        errors
    });
})
.WithName("Delete")
.WithSummary("Deletes one or more files or directories under the source folder")
.WithDescription("Accepts an array of relative paths. Directories are deleted recursively.");

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

app.Run();

public record TriggerJobRequest(string? FolderPath);
public record ForgetShowSeasonRequest(string ShowName, int SeasonNumber);
public record ForgetMovieRequest(string MovieName);
public record ForgetShowRequest(string ShowName);
public record ForgetEpisodeRequest(string ShowName, int SeasonNumber, int EpisodeNumber);
public record ForgetBatchRequest(ForgetBatchItem[] Items);
public record ForgetBatchItem(string? Type, string? MovieName, string? ShowName, int? SeasonNumber, int? EpisodeNumber);
public record RenameRequest(string Path, string NewName);
public record MoveItemRequest(string SourcePath, string DestinationFolder);
public record DeleteRequest(string[] Paths);
