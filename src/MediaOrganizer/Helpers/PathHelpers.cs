using System.Text.RegularExpressions;

namespace MediaOrganizer.Helpers;

public static class PathHelpers
{
    private static readonly Regex TrailingCopySuffixPattern = new(@"(\s+\(\d+\))+$", RegexOptions.Compiled);

    /// <summary>
    /// Strips trailing copy suffixes like " (1)", " (2)", or accumulated " (1) (1) (1)" from a filename (without extension).
    /// These suffixes are added by <see cref="EnsureUniquePath"/> and can accumulate across forget/re-organize cycles.
    /// </summary>
    public static string StripTrailingCopySuffixes(string fileNameWithoutExtension)
    {
        return TrailingCopySuffixPattern.Replace(fileNameWithoutExtension, "");
    }

    public static string EnsureUniquePath(string fullPath, IFileSystem fileSystem)
    {
        if (!fileSystem.FileExists(fullPath))
        {
            return fullPath;
        }

        var directory = Path.GetDirectoryName(fullPath)!;
        var extension = Path.GetExtension(fullPath);
        var fileName = Path.GetFileNameWithoutExtension(fullPath);

        // Strip any existing copy suffixes to avoid stacking like "name (1) (1)"
        fileName = StripTrailingCopySuffixes(fileName);

        var index = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(directory, $"{fileName} ({index}){extension}");
            index++;
        }
        while (fileSystem.FileExists(candidate));

        return candidate;
    }
}
