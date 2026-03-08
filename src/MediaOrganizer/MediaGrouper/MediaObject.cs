namespace MediaOrganizer.MediaGrouper;

public class MediaObject
{
    public required string Name { get; init; }
    public required MediaType Type { get; init; }

    /// <summary>Set when <see cref="Type"/> is <see cref="MediaType.Movie"/>.</summary>
    public string? MoviePath { get; init; }

    /// <summary>Set when <see cref="Type"/> is <see cref="MediaType.Show"/>.</summary>
    public List<Season> Seasons { get; init; } = [];
}
