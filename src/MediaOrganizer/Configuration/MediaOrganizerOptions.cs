namespace MediaOrganizer.Configuration;

public class MediaOrganizerOptions
{
    public string? SourceFolder { get; set; }

    public string? DestinationFolder { get; set; }

    public string MoveHistoryDatabasePath { get; set; } = "data/move-history.db";

    public string CronSchedule { get; set; } = "0 5 * * *";

    public string[] VideoExtensions { get; set; } =
    [
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".m4v", ".webm", ".ts", ".mpg", ".mpeg"
    ];

    public string[] SubtitleExtensions { get; set; } =
    [
        ".srt", ".sub", ".ass", ".ssa", ".vtt", ".idx"
    ];
}

