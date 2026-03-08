using MediaOrganizer.Helpers;

namespace MediaOrganizer.Cleanup;

public class DirectoryCleaner
{
    private readonly ILogger<DirectoryCleaner> _logger;
    private readonly IFileSystem _fileSystem;

    public DirectoryCleaner(ILogger<DirectoryCleaner> logger, IFileSystem fileSystem)
    {
        _logger = logger;
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Removes any directory tree under <paramref name="sourceRoot"/> that contains no video or subtitle files.
    ///
    /// A directory is only removed when neither it nor any of its descendants contain a file with an allowed
    /// video/subtitle extension. This guarantees that a parent directory that only contains a child directory
    /// with media files is not deleted.
    ///
    /// All files inside directories that are removed are deleted as part of the directory deletion.
    /// </summary>
    public int CleanupDirectoriesWithoutMedia(
        string sourceRoot,
        IEnumerable<string> videoExtensions,
        IEnumerable<string> subtitleExtensions)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot))
        {
            throw new ArgumentException("Source root must be provided.", nameof(sourceRoot));
        }

        var normalizedRoot = Path.GetFullPath(sourceRoot);
        if (!_fileSystem.DirectoryExists(normalizedRoot))
        {
            return 0;
        }

        var mediaExtensions = videoExtensions
            .Concat(subtitleExtensions)
            .Select(NormalizeExtension)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Collect all directories under the root without following symlinks.
        var allDirs = new List<string>();
        var stack = new Stack<string>();
        stack.Push(normalizedRoot);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            IEnumerable<string> children;
            try
            {
                children = _fileSystem.EnumerateDirectories(current);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enumerate directories under: {Dir}", current);
                continue;
            }

            foreach (var child in children)
            {
                if (IsSymlink(child))
                {
                    continue;
                }

                allDirs.Add(child);
                stack.Push(child);
            }
        }

        // Include root so we can compute subtree media presence easily.
        var dirsIncludingRoot = new List<string>(allDirs.Count + 1) { normalizedRoot };
        dirsIncludingRoot.AddRange(allDirs);

        // Compute whether each directory contains any media (video/subtitle) in its subtree.
        // Process deepest directories first so children are computed before parents.
        var hasMediaInSubtree = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in dirsIncludingRoot.OrderByDescending(d => d.Length))
        {
            if (!_fileSystem.DirectoryExists(dir))
            {
                hasMediaInSubtree[dir] = false;
                continue;
            }

            if (IsSymlink(dir))
            {
                // Skip symlinked directories entirely.
                hasMediaInSubtree[dir] = true;
                continue;
            }

            var hasDirectMedia = DirectoryContainsMediaFiles(dir, mediaExtensions);
            if (hasDirectMedia)
            {
                hasMediaInSubtree[dir] = true;
                continue;
            }

            var hasChildMedia = false;
            try
            {
                foreach (var child in _fileSystem.EnumerateDirectories(dir))
                {
                    if (IsSymlink(child))
                        continue;

                    if (hasMediaInSubtree.TryGetValue(child, out var childHasMedia) && childHasMedia)
                    {
                        hasChildMedia = true;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                // If we cannot enumerate, play it safe and keep the directory.
                _logger.LogWarning(ex, "Failed to enumerate child directories for: {Dir}", dir);
                hasChildMedia = true;
            }

            hasMediaInSubtree[dir] = hasChildMedia;
        }

        // Delete directories that have no media in their subtree. Deepest first.
        var deletedFileCount = 0;
        foreach (var dir in allDirs.OrderByDescending(d => d.Length))
        {
            if (!_fileSystem.DirectoryExists(dir))
                continue;

            if (IsSymlink(dir))
                continue;

            if (hasMediaInSubtree.TryGetValue(dir, out var hasMedia) && hasMedia)
            {
                continue;
            }

            try
            {
                // Count only direct files here to avoid double-counting (children are handled first).
                deletedFileCount += SafeCountFiles(dir);

                // Try non-recursive delete first (children should already be gone).
                _fileSystem.DeleteDirectory(dir, recursive: false);
                _logger.LogInformation("Removed directory with no media: {Dir}", dir);
            }
            catch (IOException)
            {
                // Fallback: delete recursively if something is still inside.
                try
                {
                    deletedFileCount += SafeCountFilesRecursive(dir);
                    _fileSystem.DeleteDirectory(dir, recursive: true);
                    _logger.LogInformation("Removed directory with no media (recursive): {Dir}", dir);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to remove directory: {Dir}", dir);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Fallback to recursive delete, then give up.
                try
                {
                    deletedFileCount += SafeCountFilesRecursive(dir);
                    _fileSystem.DeleteDirectory(dir, recursive: true);
                    _logger.LogInformation("Removed directory with no media (recursive): {Dir}", dir);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to remove directory: {Dir}", dir);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove directory: {Dir}", dir);
            }
        }

        return deletedFileCount;
    }

    /// <summary>
    /// Deletes leftover files and removes empty directories in source folders
    /// that had files moved out of them. Processes deepest directories first.
    /// </summary>
    public int CleanupSourceDirectories(IEnumerable<string> movedSourcePaths, string sourceRoot)
    {
        var normalizedRoot = Path.GetFullPath(sourceRoot);
        var deletedFileCount = 0;

        // Collect all directories that had files moved from them
        var affectedDirs = movedSourcePaths
            .Select(p => Path.GetDirectoryName(Path.GetFullPath(p))!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Include ancestor directories up to (but not including) the source root
        var allDirsToCheck = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in affectedDirs)
        {
            var current = dir;
            while (current != null
                   && !string.Equals(current, normalizedRoot, StringComparison.OrdinalIgnoreCase)
                   && current.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                allDirsToCheck.Add(current);
                current = Path.GetDirectoryName(current);
            }
        }

        // Process deepest directories first (longest path = deepest)
        foreach (var dir in allDirsToCheck.OrderByDescending(d => d.Length))
        {
            if (!_fileSystem.DirectoryExists(dir))
                continue;

            // Delete remaining files
            foreach (var file in _fileSystem.GetFiles(dir))
            {
                try
                {
                    _fileSystem.DeleteFile(file);
                    deletedFileCount++;
                    _logger.LogInformation("Deleted leftover file: {File}", file);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete leftover file: {File}", file);
                }
            }

            // Remove directory if now empty
            try
            {
                if (!_fileSystem.HasFileSystemEntries(dir))
                {
                    _fileSystem.DeleteDirectory(dir, recursive: false);
                    _logger.LogInformation("Removed empty directory: {Dir}", dir);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove directory: {Dir}", dir);
            }
        }

        return deletedFileCount;
    }

    private bool DirectoryContainsMediaFiles(string dir, HashSet<string> mediaExtensions)
    {
        try
        {
            foreach (var file in _fileSystem.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
            {
                if (mediaExtensions.Contains(Path.GetExtension(file)))
                {
                    return true;
                }
            }
        }
        catch
        {
            // If we cannot enumerate files, treat it as containing media to avoid deletion.
            return true;
        }

        return false;
    }

    private bool IsSymlink(string path)
    {
        try
        {
            return _fileSystem.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            // If attributes cannot be read, treat as symlink-ish to avoid deletion.
            return true;
        }
    }

    private int SafeCountFiles(string dir)
    {
        try
        {
            return _fileSystem.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly).Count();
        }
        catch
        {
            return 0;
        }
    }

    private int SafeCountFilesRecursive(string dir)
    {
        try
        {
            return _fileSystem.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Count();
        }
        catch
        {
            return 0;
        }
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        return extension.StartsWith('.')
            ? extension.ToLowerInvariant()
            : $".{extension.ToLowerInvariant()}";
    }
}
