using MediaOrganizer.MediaGrouper;
using MediaOrganizer.MoveHistory;

using Microsoft.EntityFrameworkCore;

namespace MediaOrganizer.MovePlan;

/// <summary>
/// Builds a move plan from grouped media files, checking against move history to determine which files need to be moved.
/// Handles logic for:
/// - Detecting files that have already been moved successfully
/// - Detecting files with changed destination paths
/// - Returning only files that need to be moved
/// </summary>
public class MovePlanBuilder
{
    private readonly ILogger<MovePlanBuilder> _logger;
    private readonly IDbContextFactory<MoveHistoryDbContext> _contextFactory;

    public MovePlanBuilder(
        ILogger<MovePlanBuilder> logger,
        IDbContextFactory<MoveHistoryDbContext> contextFactory)
    {
        _logger = logger;
        _contextFactory = contextFactory;
    }

    /// <summary>
    /// Builds a move plan from grouped media files and updates the database records.
    /// Handles:
    /// - Already moved files (destination same, ismoved=true) -> ignored
    /// - Unmoved files with same destination (ismoved=false) -> kept in database
    /// - Files with changed destination -> new record created with ismoved=false
    /// </summary>
    /// <param name="mediaObjects">The grouped media objects from MediaGrouper</param>
    /// <param name="rootFolder">The root destination folder for organizing media</param>
    public void BuildMovePlan(List<MediaObject> mediaObjects, string rootFolder)
    {

        using var context = _contextFactory.CreateDbContext();

        foreach (var media in mediaObjects)
        {
            var filesToMove = GetFilesForMediaObject(media);

            foreach (var filePair in filesToMove)
            {
                var filePath = filePair.FilePath;
                var uniqueKey = filePair.UniqueKey;
                var destinationPath = ResolveDestinationPath(filePath, media, rootFolder);

                // Get latest move history record for this unique key
                var latestRecord = context.MoveHistory
                    .Where(e => e.UniqueKey == uniqueKey)
                    .OrderByDescending(e => e.Id)
                    .FirstOrDefault();

                if (latestRecord != null)
                {
                    // File has history
                    if (string.Equals(latestRecord.TargetFilePath, destinationPath, StringComparison.OrdinalIgnoreCase))
                    {
                        // Destination is the same
                        if (latestRecord.IsMoved)
                        {
                            // Already moved successfully - ignore
                            _logger.LogInformation(
                                "File already moved successfully. Original: {Original} -> {Target}",
                                filePath,
                                destinationPath);
                            continue;
                        }

                        // Not yet moved, but same destination - already in database, no action needed
                    }
                    else
                    {
                        // Destination changed - create new record and add to plan
                        _logger.LogInformation(
                            "Destination path changed for {File}. Old: {OldDest} -> New: {NewDest}",
                            filePath,
                            latestRecord.TargetFilePath,
                            destinationPath);

                        var newEntry = new MoveHistoryEntry
                        {
                            UniqueKey = uniqueKey,
                            OriginalFilePath = filePath,
                            TargetFilePath = destinationPath,
                            MoveDateTime = DateTime.UtcNow,
                            IsMoved = false
                        };

                        context.MoveHistory.Add(newEntry);
                    }
                }
                else
                {
                    // New file not in history - create entry and add to plan
                    var newEntry = new MoveHistoryEntry
                    {
                        UniqueKey = uniqueKey,
                        OriginalFilePath = filePath,
                        TargetFilePath = destinationPath,
                        MoveDateTime = DateTime.UtcNow,
                        IsMoved = false
                    };

                    context.MoveHistory.Add(newEntry);
                }
            }
        }

        context.SaveChanges();

        _logger.LogInformation("Move plan database updated");
    }

    /// <summary>
    /// Gets all file paths for a given media object (handles both movies and shows).
    /// </summary>
    private static List<FileKeyPair> GetFilesForMediaObject(MediaObject media)
    {
        var files = new List<FileKeyPair>();

        if (media.Type == MediaType.Movie && media.MoviePath != null)
        {
            files.Add(new FileKeyPair(media.MoviePath, media.Name));
        }
        else if (media.Type == MediaType.Show)
        {
            foreach (var season in media.Seasons)
            {
                var seasonKey = $"{media.Name}_Season{season.SeasonNumber:00}";
                files.AddRange(season.Episodes.Select(e => new FileKeyPair(e.Path, $"{seasonKey}_Episode{e.EpisodeNumber:00}")));
            }
        }

        return files;
    }

    private record FileKeyPair(string FilePath, string UniqueKey);

    /// <summary>
    /// Resolves the destination path for a file based on the media object and root folder.
    /// Follows the folder structure rules for movies and shows.
    /// </summary>
    private static string ResolveDestinationPath(string filePath, MediaObject media, string rootFolder)
    {
        var extension = Path.GetExtension(filePath);

        if (media.Type == MediaType.Movie)
        {
            // Movie structure: {root}/{MovieName}/{MovieName}.ext
            var movieFolder = Path.Combine(rootFolder, media.Name);
            return Path.Combine(movieFolder, $"{media.Name}{extension}");
        }

        // Show structure: {root}/{ShowName}/Season {season}/{EpisodeName}.ext
        // Find which season this episode belongs to
        foreach (var season in media.Seasons)
        {
            if (season.Episodes.Any(e => e.Path == filePath))
            {
                var seasonFolder = Path.Combine(rootFolder, media.Name, $"Season {season.SeasonNumber:00}");
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                return Path.Combine(seasonFolder, $"{fileName}{extension}");
            }
        }

        // Fallback - shouldn't reach here if media object is properly constructed
        throw new InvalidOperationException(
            $"Could not determine destination path for file {filePath} in media object {media.Name}");
    }
}
