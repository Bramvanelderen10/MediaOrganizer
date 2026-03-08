namespace MediaOrganizer.MediaGrouper;

public class Season(int seasonNumber, List<Episode> episodes)
{
    public int SeasonNumber { get; } = seasonNumber;
    public List<Episode> Episodes { get; } = episodes;
}
