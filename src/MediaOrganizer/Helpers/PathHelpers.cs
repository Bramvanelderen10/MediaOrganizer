namespace MediaOrganizer;

public static class PathHelpers
{
    public static string EnsureUniquePath(string fullPath)
    {
        if (!File.Exists(fullPath))
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
        while (File.Exists(candidate));

        return candidate;
    }
}
