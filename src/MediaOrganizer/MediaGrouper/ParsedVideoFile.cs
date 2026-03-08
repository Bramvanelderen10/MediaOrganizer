namespace MediaOrganizer;

public record ParsedVideoFile(
    string FilePath,
    string Title,
    string CleanedFileName,
    int? Season,
    int? Episode,
    string? ParentFolderCleanName);
