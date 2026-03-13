using Microsoft.EntityFrameworkCore;

namespace MediaOrganizer.Settings;

public class SettingStore
{
    private readonly ILogger<SettingStore> _logger;
    private readonly IDbContextFactory<SettingEntriesDbContext> _contextFactory;

    public SettingStore(
        ILogger<SettingStore> logger,
        IDbContextFactory<SettingEntriesDbContext> contextFactory)
    {
        _logger = logger;
        _contextFactory = contextFactory;
        EnsureDatabase();
    }

    private void EnsureDatabase()
    {
        using var context = _contextFactory.CreateDbContext();
        context.Database.EnsureCreated();
    }

    public void SaveSetting(SettingKey key, string value)
    {
        using var context = _contextFactory.CreateDbContext();

        var existingEntry = context.Settings.FirstOrDefault(e => e.Key == key);
        if (existingEntry != null)
        {
            existingEntry.Value = value;
            context.Settings.Update(existingEntry);
        }
        else
        {
            var newEntry = new SettingEntry
            {
                Key = key,
                Value = value
            };
            context.Settings.Add(newEntry);
        }

        context.SaveChanges();
        _logger.LogInformation("Saved setting {Key} to database", key);
    }

    public bool IsEnabled(SettingKey key)
    {
        using var context = _contextFactory.CreateDbContext();

        var entry = context.Settings.FirstOrDefault(e => e.Key == key);
        if (entry != null && bool.TryParse(entry.Value, out var result))
        {
            return result;
        }

        _logger.LogInformation("Setting {Key} not found in database, defaulting to false", key);
        return false;
    }
}

public enum SettingKey
{
    IsJobSchedulerEnabled,
}