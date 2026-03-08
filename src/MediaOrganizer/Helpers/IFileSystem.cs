namespace MediaOrganizer.Helpers;

/// <summary>
/// Abstracts file-system operations so that classes relying on the file system
/// can be unit-tested with a mock implementation.
/// </summary>
public interface IFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    void CreateDirectory(string path);
    void MoveFile(string source, string destination);
    void DeleteFile(string path);
    void DeleteDirectory(string path, bool recursive);
    IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption);
    IEnumerable<string> EnumerateDirectories(string path);
    string[] GetFiles(string path);
    bool HasFileSystemEntries(string path);
    FileAttributes GetAttributes(string path);
}
