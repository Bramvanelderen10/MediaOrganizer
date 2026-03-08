using MediaOrganizer.Helpers;

namespace MediaOrganizer.Discovery;

public class VideoFileFinder
{
    private readonly IFileSystem _fileSystem;

    public VideoFileFinder(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public IReadOnlyList<string> GetVideoFiles(string sourceFolder, IEnumerable<string> extensions)
    {
        var allowedExtensions = extensions
            .Select(NormalizeExtension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var videoFilePaths = new List<string>();

        foreach (var path in _fileSystem.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories))
        {
            if (IsHiddenFile(path))
            {
                _fileSystem.DeleteFile(path);
                continue;
            }

            if (!allowedExtensions.Contains(Path.GetExtension(path)))
            {
                continue;
            }

            videoFilePaths.Add(path);
        }

        return videoFilePaths;
    }

    private static bool IsHiddenFile(string fullPath)
    {
        var fileName = Path.GetFileName(fullPath);
        return fileName.StartsWith(".", StringComparison.Ordinal)
            && fileName.Contains("trash", StringComparison.OrdinalIgnoreCase);
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
