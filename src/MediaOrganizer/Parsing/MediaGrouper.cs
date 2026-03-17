using System.Text.RegularExpressions;

namespace MediaOrganizer.Parsing;

public class MediaGrouper
{
    private static readonly Regex BracketPattern = new(@"\[[^\]]*\]|\([^\)]*\)", RegexOptions.Compiled);
    private static readonly Regex ResolutionPattern = new(@"\b(2160|1080|720|480|360)[pi]\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CodecPattern = new(@"\b(x264|x265|h\.?264|h\.?265|HEVC|AVC|AAC|DTS|FLAC|10bit|8bit)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ReleaseGroupPattern = new(@"\b(ELiTE|YIFY|BONE|AAC5|RARBG|FGT|LOL|ETTV|EZTVx?|SubsPlease|BluRay|BRRip|WEBRip|WEB[\-\.]?DL|HDRip|DVDRip|Dual\s*Audio|PROPER|REPACK|EMBER|Cleo|INTERNAL)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex VersionTagPattern = new(@"\b[Vv]\d+\b", RegexOptions.Compiled);
    private static readonly Regex SeasonEpisodePattern = new(@"\bSE?(?<season>\d{1,2})\s*E(?<episode>\d{1,4})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SeasonDashEpisodePattern = new(@"\bS(?<season>\d{1,2})(?:\s*[-–—]\s*|\s+)(?<episode>\d{1,4})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TrailingNumberPattern = new(@"\s+(?<episode>\d{1,4})\s*$", RegexOptions.Compiled);
    private static readonly Regex MultiSpacePattern = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex SeasonFolderPattern = new(@"\bSeason\s*(?<season>\d{1,2})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex FolderSeasonMarkerPattern = new(@"\bS\d{1,2}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private const double SimilarityThreshold = 0.80;

    /// <summary>
    /// Groups video files by smart name matching and returns structured <see cref="MediaObject"/> results.
    /// Files are classified as Movie (single file, no episode info) or Show (multiple files or SxxExx detected).
    /// </summary>
    public List<MediaObject> GroupMediaFiles(IReadOnlyList<string> allVideoFiles)
    {
        var parsedFiles = allVideoFiles.Select(ParseVideoFile).ToList();
        parsedFiles = ApplyFolderBasedTitleOverrides(parsedFiles);
        var groups = GroupBySimilarTitle(parsedFiles);

        var mediaObjects = groups.Select(BuildMediaObject).ToList();
        return mediaObjects;
    }

    // ────────────────────────── ── Parsing ────────────────────────────

    private ParsedVideoFile ParseVideoFile(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var cleaned = CleanName(fileName);

        var parentFolderCleanName = GetCleanParentFolderName(filePath);

        // Try SxxExx first (e.g. S01E07)
        var seMatch = SeasonEpisodePattern.Match(cleaned);
        if (seMatch.Success)
        {
            var season = int.Parse(seMatch.Groups["season"].Value);
            var episode = int.Parse(seMatch.Groups["episode"].Value);
            var title = NormalizeSpaces(cleaned[..seMatch.Index]);

            // Fall back to parent folder name when the title is empty
            // (e.g. filename starts with SxxExx like "S02E06-Episode Name")
            if (string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(parentFolderCleanName))
            {
                title = ExtractTitleFromFolderName(parentFolderCleanName);
            }

            return new ParsedVideoFile(filePath, title, cleaned, season, episode, parentFolderCleanName);
        }

        // Try Sxx - xx (e.g. S2 - 03)
        var sdMatch = SeasonDashEpisodePattern.Match(cleaned);
        if (sdMatch.Success)
        {
            var season = int.Parse(sdMatch.Groups["season"].Value);
            var episode = int.Parse(sdMatch.Groups["episode"].Value);
            var title = NormalizeSpaces(cleaned[..sdMatch.Index]);

            if (string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(parentFolderCleanName))
                title = parentFolderCleanName;

            return new ParsedVideoFile(filePath, title, cleaned, season, episode, parentFolderCleanName);
        }

        // Try trailing episode number (e.g. "Jujutsu Kaisen 58")
        var epMatch = TrailingNumberPattern.Match(cleaned);
        if (epMatch.Success)
        {
            var episode = int.Parse(epMatch.Groups["episode"].Value);
            var title = NormalizeSpaces(cleaned[..epMatch.Index]);

            return new ParsedVideoFile(filePath, title, cleaned, null, episode, parentFolderCleanName);
        }

        // No episode info detected
        return new ParsedVideoFile(filePath, NormalizeSpaces(cleaned), cleaned, null, null, parentFolderCleanName);
    }

    private string CleanName(string name)
    {
        // 1. Remove anything inside [] or ()
        var result = BracketPattern.Replace(name, " ");
        // 2. Remove resolution tags (1080p, 720p, …)
        result = ResolutionPattern.Replace(result, " ");
        // 3. Remove codec tags (x265, HEVC, …)
        result = CodecPattern.Replace(result, " ");
        // 4. Remove known release-group / source tags
        result = ReleaseGroupPattern.Replace(result, " ");
        // 5. Remove version tags (V2, V3, …)
        result = VersionTagPattern.Replace(result, " ");
        // 6. Normalize separators
        result = result.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');

        return NormalizeSpaces(result);
    }

    private string? GetCleanParentFolderName(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(dir))
            return null;

        var folderName = Path.GetFileName(dir);
        if (string.IsNullOrEmpty(folderName))
            return null;

        // If the immediate parent is a "Season XX" folder, use the grandparent instead
        if (SeasonFolderPattern.IsMatch(folderName))
        {
            var grandparentDir = Path.GetDirectoryName(dir);
            if (!string.IsNullOrEmpty(grandparentDir))
            {
                var grandparentName = Path.GetFileName(grandparentDir);
                if (!string.IsNullOrEmpty(grandparentName))
                    folderName = grandparentName;
            }
        }

        var cleaned = NormalizeSpaces(CleanName(folderName));
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private static string NormalizeSpaces(string value)
        => MultiSpacePattern.Replace(value, " ").Trim();

    private static string ExtractTitleFromFolderName(string folderCleanName)
    {
        var seasonMatch = SeasonFolderPattern.Match(folderCleanName);
        if (seasonMatch.Success && seasonMatch.Index > 0)
            return folderCleanName[..seasonMatch.Index].Trim();

        var markerMatch = FolderSeasonMarkerPattern.Match(folderCleanName);
        if (markerMatch.Success && markerMatch.Index > 0)
            return folderCleanName[..markerMatch.Index].Trim();

        return folderCleanName;
    }

    // ──────────────────────────── Pre-grouping ────────────────────────────

    /// <summary>
    /// For files sharing the same parent folder and season, if their titles are incoherent
    /// (no single title is similar to the majority), override all titles with the parent folder name.
    /// This handles cases where filenames contain episode descriptions instead of show names.
    /// </summary>
    private static List<ParsedVideoFile> ApplyFolderBasedTitleOverrides(List<ParsedVideoFile> files)
    {
        // Group by (parent directory, season) — only files with a parsed season
        var folderSeasonGroups = files
            .Where(f => f.Season.HasValue)
            .GroupBy(f => (Dir: Path.GetDirectoryName(f.FilePath) ?? "", Season: f.Season!.Value));

        var overrides = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in folderSeasonGroups)
        {
            var items = group.ToList();
            if (items.Count < 2)
                continue;

            // Check if there's a coherent title cluster:
            // any title that is similar to at least 50% of the other titles
            var hasCoherentCluster = items.Any(candidate =>
            {
                var matchCount = items.Count(other => AreTitlesSimilar(candidate.Title, other.Title));
                return matchCount * 2 >= items.Count;
            });

            if (!hasCoherentCluster)
            {
                foreach (var item in items)
                    overrides.Add(item.FilePath);
            }
        }

        if (overrides.Count == 0)
            return files;

        return files.Select(f =>
            overrides.Contains(f.FilePath) && !string.IsNullOrWhiteSpace(f.ParentFolderCleanName)
                ? f with { Title = f.ParentFolderCleanName }
                : f
        ).ToList();
    }

    // ──────────────────────────── Grouping ────────────────────────────

    private static List<List<ParsedVideoFile>> GroupBySimilarTitle(List<ParsedVideoFile> files)
    {
        var groups = new List<List<ParsedVideoFile>>();
        var assigned = new bool[files.Count];

        for (var i = 0; i < files.Count; i++)
        {
            if (assigned[i])
                continue;

            var group = new List<ParsedVideoFile> { files[i] };
            assigned[i] = true;

            for (var j = i + 1; j < files.Count; j++)
            {
                if (assigned[j])
                    continue;

                if (AreTitlesSimilar(files[i].Title, files[j].Title))
                {
                    group.Add(files[j]);
                    assigned[j] = true;
                }
            }

            groups.Add(group);
        }

        return groups;
    }

    // ──────────────────────────── Similarity ────────────────────────────

    private static bool AreTitlesSimilar(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            return true;

        return ComputeSimilarity(a.ToLowerInvariant(), b.ToLowerInvariant()) >= SimilarityThreshold;
    }

    private static double ComputeSimilarity(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0)
            return 1.0;
        if (a.Length == 0 || b.Length == 0)
            return 0.0;

        var distance = LevenshteinDistance(a, b);
        return 1.0 - (double)distance / Math.Max(a.Length, b.Length);
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var m = a.Length;
        var n = b.Length;
        var dp = new int[m + 1, n + 1];

        for (var i = 0; i <= m; i++)
            dp[i, 0] = i;
        for (var j = 0; j <= n; j++)
            dp[0, j] = j;

        for (var i = 1; i <= m; i++)
            for (var j = 1; j <= n; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + cost);
            }

        return dp[m, n];
    }

    // ──────────────────────────── Building MediaObjects ────────────────────────────

    private MediaObject BuildMediaObject(List<ParsedVideoFile> group)
    {
        var hasSeasonEpisode = group.Any(f => f.Season.HasValue);
        var isMultiFile = group.Count > 1;

        // Single file without SxxExx → Movie
        if (!hasSeasonEpisode && !isMultiFile)
        {
            // Use the full cleaned filename so trailing numbers stay part of the title
            var movieName = group[0].CleanedFileName;
            return new MediaObject
            {
                Name = movieName,
                Type = MediaType.Movie,
                MoviePath = group[0].FilePath
            };
        }

        // Otherwise it's a Show
        var canonicalName = DetermineCanonicalName(group);
        var seasons = BuildSeasons(group);

        return new MediaObject
        {
            Name = canonicalName,
            Type = MediaType.Show,
            Seasons = seasons
        };
    }

    private List<Season> BuildSeasons(List<ParsedVideoFile> group)
    {
        // Bucket files by season number (files without an explicit season default to 1)
        var buckets = new Dictionary<int, List<(ParsedVideoFile File, int? Episode)>>();

        foreach (var file in group)
        {
            var seasonNum = file.Season ?? 1;
            if (!buckets.TryGetValue(seasonNum, out var bucket))
            {
                bucket = [];
                buckets[seasonNum] = bucket;
            }

            bucket.Add((file, file.Episode));
        }

        var result = new List<Season>();

        foreach (var (seasonNum, entries) in buckets.OrderBy(kv => kv.Key))
        {
            var withEpisode = entries.Where(e => e.Episode.HasValue).ToList();
            var withoutEpisode = entries.Where(e => !e.Episode.HasValue).ToList();

            var ordered = new List<(ParsedVideoFile File, int Episode)>();

            // Keep files that already have an episode number
            foreach (var e in withEpisode)
                ordered.Add((e.File, e.Episode!.Value));

            // Assign episode numbers to the rest (alphabetical by filename)
            if (withoutEpisode.Count > 0)
            {
                var maxEp = ordered.Count > 0 ? ordered.Max(e => e.Episode) : 0;
                var sorted = withoutEpisode
                    .OrderBy(e => Path.GetFileName(e.File.FilePath), StringComparer.OrdinalIgnoreCase);

                foreach (var e in sorted)
                {
                    maxEp++;
                    ordered.Add((e.File, maxEp));
                }
            }

            var episodes = ordered
                .OrderBy(e => e.Episode)
                .Select(e => new Episode(e.File.FilePath, e.Episode))
                .ToList();

            result.Add(new Season(seasonNum, episodes));
        }

        return result;
    }

    private static string DetermineCanonicalName(List<ParsedVideoFile> group)
    {
        // Prefer a clean parent-folder name when it matches the majority of titles.
        // This picks up existing well-named folders (e.g. "Jujutsu Kaisen/" even when
        // individual filenames contain typos like "Jujustu Kaisen").
        var folderNames = group
            .Where(f => !string.IsNullOrWhiteSpace(f.ParentFolderCleanName))
            .Select(f => f.ParentFolderCleanName!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var folderName in folderNames)
        {
            var matchCount = group.Count(f => AreTitlesSimilar(f.Title, folderName));
            if (matchCount * 2 >= group.Count)
                return folderName;
        }

        // Fall back to the most common title in the group
        return group
            .GroupBy(f => f.Title, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Key.Length)
            .First().Key;
    }
}
