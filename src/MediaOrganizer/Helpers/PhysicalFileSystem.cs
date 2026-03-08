namespace MediaOrganizer.Helpers;

/// <summary>
/// Default <see cref="IFileSystem"/> implementation that delegates to the real
/// <see cref="File"/> and <see cref="Directory"/> static methods.
/// </summary>
public class PhysicalFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void MoveFile(string source, string destination) => File.Move(source, destination);

    public void DeleteFile(string path) => File.Delete(path);

    public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);

    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
        => Directory.EnumerateFiles(path, searchPattern, searchOption);

    public IEnumerable<string> EnumerateDirectories(string path)
        => Directory.EnumerateDirectories(path);

    public string[] GetFiles(string path) => Directory.GetFiles(path);

    public bool HasFileSystemEntries(string path)
        => Directory.EnumerateFileSystemEntries(path).Any();

    public FileAttributes GetAttributes(string path) => File.GetAttributes(path);
}
