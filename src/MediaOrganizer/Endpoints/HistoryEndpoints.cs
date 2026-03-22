using System.Text.RegularExpressions;

using MediaOrganizer.History;

namespace MediaOrganizer.Endpoints;

public static class HistoryEndpoints
{
    public static void MapHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/forget-show-season", (MoveHistoryStore moveHistoryStore, ForgetShowSeasonRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.ShowName))
            {
                return Results.BadRequest(new { message = "showName is required" });
            }

            if (request.SeasonNumber <= 0)
            {
                return Results.BadRequest(new { message = "seasonNumber must be greater than 0" });
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
    }
}
