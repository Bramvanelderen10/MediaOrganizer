namespace MediaOrganizer;

public class Season(int seasonNumber, List<string> episodePaths)
{
    public int SeasonNumber { get; } = seasonNumber;
    public List<string> EpisodePaths { get; } = episodePaths;
}
