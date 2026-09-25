namespace MediaOrganizer.Configuration;

public class MediaOrganizerOptions
{
    public string? SourceFolder { get; set; }

    public string? DestinationFolder { get; set; }

    public string MoveHistoryDatabasePath { get; set; } = "data/move-history.db";

    public string[] VideoExtensions { get; set; } =
    [
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".m4v", ".webm", ".ts", ".mpg", ".mpeg"
    ];

    public string[] SubtitleExtensions { get; set; } =
    [
        ".srt", ".sub", ".ass", ".ssa", ".vtt", ".idx"
    ];

    public QbittorrentOptions Qbittorrent { get; set; } = new();

    /// <summary>
    /// Optional ffmpeg transcoding step that converts unsupported codecs (e.g. HEVC) to a
    /// codec the host hardware can play (H.264). Disabled by default.
    /// </summary>
    public TranscodingOptions Transcoding { get; set; } = new();
}

