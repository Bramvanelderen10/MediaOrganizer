namespace MediaOrganizer.Settings;

public class SettingEntry
{
    public long Id { get; set; }
    public required SettingKey Key { get; set; }
    public required string Value { get; set; } = null!;
}
