namespace MediaOrganizer.Endpoints;

public record TriggerJobRequest(string? FolderPath);
public record ForgetShowSeasonRequest(string ShowName, int SeasonNumber);
public record ForgetMovieRequest(string MovieName);
public record ForgetShowRequest(string ShowName);
public record ForgetEpisodeRequest(string ShowName, int SeasonNumber, int EpisodeNumber);
public record ForgetBatchRequest(ForgetBatchItem[] Items);
public record ForgetBatchItem(string? Type, string? MovieName, string? ShowName, int? SeasonNumber, int? EpisodeNumber);
public record RenameRequest(string Path, string NewName);
public record MoveItemRequest(string SourcePath, string DestinationFolder);
public record DeleteRequest(string[] Paths);
