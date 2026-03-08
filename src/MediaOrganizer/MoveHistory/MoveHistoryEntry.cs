namespace MediaOrganizer.MoveHistory;

public class MoveHistoryEntry
{
    public long Id { get; set; }

    public required string UniqueKey { get; set; }

    public required string OriginalFilePath { get; set; }

    public required string TargetFilePath { get; set; }

    public DateTime MoveDateTime { get; set; } = DateTime.UtcNow;

    public bool IsMoved { get; set; }
}
