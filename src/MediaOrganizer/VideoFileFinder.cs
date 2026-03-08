namespace MediaOrganizer;

public class VideoFileFinder
{
    public IReadOnlyList<string> GetVideoFiles(string sourceFolder, IEnumerable<string> extensions)
    {
        var allowedExtensions = extensions
            .Select(NormalizeExtension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Directory
            .EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories)
            .Where(path => allowedExtensions.Contains(Path.GetExtension(path)))
            .ToList();
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
