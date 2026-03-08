namespace MediaOrganizer.Helpers;

public static class PathHelpers
{
    public static string EnsureUniquePath(string fullPath, IFileSystem fileSystem)
    {
        if (!fileSystem.FileExists(fullPath))
        {
            return fullPath;
        }

        var directory = Path.GetDirectoryName(fullPath)!;
        var extension = Path.GetExtension(fullPath);
        var fileName = Path.GetFileNameWithoutExtension(fullPath);

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
