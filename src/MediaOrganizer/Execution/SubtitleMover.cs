using System.Text.RegularExpressions;

using MediaOrganizer.Helpers;

namespace MediaOrganizer.Execution;

public class SubtitleMover
{
    private readonly ILogger<SubtitleMover> _logger;
    private readonly IFileSystem _fileSystem;

    public SubtitleMover(
        ILogger<SubtitleMover> logger,
        IFileSystem fileSystem)
    {
        _logger = logger;
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Finds subtitle files in the same directories as moved videos and moves them
    /// to the corresponding destination directory.
    /// </summary>
    public List<MovedFileInfo> MoveCompanionSubtitles(
        List<MovedFileInfo> movedVideos,
        string[] subtitleExtensions,
        string sourceRoot)
    {
        var subtitleExts = new HashSet<string>(subtitleExtensions, StringComparer.OrdinalIgnoreCase);
        var movedSubtitles = new List<MovedFileInfo>();
        var normalizedRoot = Path.GetFullPath(sourceRoot);

        // Group moved videos by their original parent directory
        var bySourceDir = movedVideos
            .GroupBy(m => Path.GetDirectoryName(m.OriginalPath)!, StringComparer.OrdinalIgnoreCase);

        foreach (var group in bySourceDir)
        {
            var sourceDir = group.Key;
            if (!_fileSystem.DirectoryExists(sourceDir))
                continue;

            // Don't scan recursively if video was directly in the source root
            var searchOption = string.Equals(Path.GetFullPath(sourceDir), normalizedRoot, StringComparison.OrdinalIgnoreCase)
                ? SearchOption.TopDirectoryOnly
                : SearchOption.AllDirectories;

            var subtitleFiles = _fileSystem.EnumerateFiles(sourceDir, "*", searchOption)
                .Where(f => subtitleExts.Contains(Path.GetExtension(f)))
                .ToList();

            if (subtitleFiles.Count == 0)
                continue;

            var videos = group.ToList();

            foreach (var subFile in subtitleFiles)
            {
                if (!_fileSystem.FileExists(subFile))
                    continue;

                var targetVideo = videos.Count == 1
                    ? videos[0]
                    : FindBestVideoMatch(subFile, videos) ?? videos[0];

                var destDir = Path.GetDirectoryName(targetVideo.DestinationPath)!;
                var videoStem = Path.GetFileNameWithoutExtension(targetVideo.DestinationPath);
                var subFileName = Path.GetFileName(subFile);

                // Prefix subtitle with the video name so files from different episodes
                // don't collide (e.g. "Health Care S01E03.2_English.srt").
                // Skip the prefix when the subtitle already contains the video's name.
                if (!Path.GetFileNameWithoutExtension(subFileName)
                         .Contains(videoStem, StringComparison.OrdinalIgnoreCase))
                {
                    subFileName = $"{videoStem}.{subFileName}";
                }

                var destPath = Path.Combine(destDir, subFileName);
                destPath = PathHelpers.EnsureUniquePath(destPath, _fileSystem);

                _fileSystem.CreateDirectory(destDir);
                _fileSystem.MoveFile(subFile, destPath);
                movedSubtitles.Add(new MovedFileInfo(subFile, destPath));
                _logger.LogInformation("Moved subtitle '{Source}' -> '{Destination}'", subFile, destPath);
            }
        }

        return movedSubtitles;
    }

    /// <summary>
    /// Matches a subtitle file to the best-fitting video based on SxxExx patterns
    /// or filename similarity.
    /// </summary>
    private static MovedFileInfo? FindBestVideoMatch(string subtitlePath, List<MovedFileInfo> videos)
    {
        var subName = Path.GetFileNameWithoutExtension(subtitlePath);

        // Try matching by SxxExx pattern in the subtitle filename first
        var subSeMatch = Regex.Match(subName, @"S\d{1,2}E\d{1,4}", RegexOptions.IgnoreCase);

        // If filename has no SxxExx, check parent directory names (handles Subs/Show.S01E01.../2_English.srt)
        if (!subSeMatch.Success)
        {
            var dir = Path.GetDirectoryName(subtitlePath);
            while (!string.IsNullOrEmpty(dir))
            {
                var dirName = Path.GetFileName(dir);
                subSeMatch = Regex.Match(dirName, @"S\d{1,2}E\d{1,4}", RegexOptions.IgnoreCase);
                if (subSeMatch.Success)
                    break;
                dir = Path.GetDirectoryName(dir);
            }
        }

        if (subSeMatch.Success)
        {
            var subPattern = subSeMatch.Value;
            foreach (var video in videos)
            {
                var videoName = Path.GetFileNameWithoutExtension(video.OriginalPath);
                if (videoName.Contains(subPattern, StringComparison.OrdinalIgnoreCase))
                    return video;
            }
        }

        // Try matching by longest common prefix of normalized names
        MovedFileInfo? bestMatch = null;
        var bestLength = 0;

        foreach (var video in videos)
        {
            var videoBaseName = NormalizeName(Path.GetFileNameWithoutExtension(video.OriginalPath));
            var subBaseName = NormalizeName(subName);

            var commonLength = CommonPrefixLength(videoBaseName, subBaseName);
            if (commonLength > bestLength)
            {
                bestLength = commonLength;
                bestMatch = video;
            }
        }

        return bestMatch;
    }

    private static string NormalizeName(string name)
        => name.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ').ToLowerInvariant();

    private static int CommonPrefixLength(string a, string b)
    {
        var minLength = Math.Min(a.Length, b.Length);
        for (var i = 0; i < minLength; i++)
        {
            if (a[i] != b[i])
                return i;
        }
        return minLength;
    }
}