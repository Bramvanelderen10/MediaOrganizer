using Microsoft.EntityFrameworkCore;

namespace MediaOrganizer;

public class MoveHistoryStore
{
    private readonly ILogger<MoveHistoryStore> _logger;
    private readonly IDbContextFactory<MoveHistoryDbContext> _contextFactory;

    public MoveHistoryStore(
        ILogger<MoveHistoryStore> logger,
        IDbContextFactory<MoveHistoryDbContext> contextFactory)
    {
        _logger = logger;
        _contextFactory = contextFactory;
        EnsureDatabase();
    }

    /// <summary>
    /// Saves entries from the move plan to the database.
    /// These entries are created by MovePlanBuilder and represent files that need to be moved.
    /// </summary>
    public void SaveMovePlanEntries(IReadOnlyCollection<MovePlanItem> planItems)
    {
        if (planItems.Count == 0)
        {
            return;
        }

        using var context = _contextFactory.CreateDbContext();

        var entries = planItems.Select(item => new MoveHistoryEntry
        {
            UniqueKey = item.UniqueKey,
            OriginalFilePath = item.OriginalFilePath,
            TargetFilePath = item.TargetFilePath,
            MoveDateTime = DateTime.UtcNow,
            IsMoved = false
        }).ToList();

        context.MoveHistory.AddRange(entries);
        context.SaveChanges();

        _logger.LogInformation("Saved {Count} move plan entries to database", entries.Count);
    }

    /// <summary>
    /// Retrieves all entries that need to be moved (those with IsMoved = false).
    /// These are the entries that ExecuteMovePlan should process.
    /// </summary>
    public IReadOnlyList<MoveHistoryEntry> GetEntriesNeedingMove()
    {
        using var context = _contextFactory.CreateDbContext();
        return context.MoveHistory
            .Where(e => !e.IsMoved)
            .OrderBy(e => e.Id)
            .ToList();
    }

    public void UpdateTargetPath(long id, string targetPath)
    {
        using var context = _contextFactory.CreateDbContext();
        context.MoveHistory
            .Where(e => e.Id == id)
            .ExecuteUpdate(s => s.SetProperty(e => e.TargetFilePath, targetPath));
    }

    public void UpdateIsMoved(long id, bool isMoved)
    {
        using var context = _contextFactory.CreateDbContext();
        context.MoveHistory
            .Where(e => e.Id == id)
            .ExecuteUpdate(s => s.SetProperty(e => e.IsMoved, isMoved));
    }

    public IReadOnlyList<MoveHistoryEntry> GetMovedEntriesForRestore()
    {
        using var context = _contextFactory.CreateDbContext();
        return context.MoveHistory
            .Where(e => e.IsMoved)
            .OrderByDescending(e => e.Id)
            .ToList();
    }

    private void EnsureDatabase()
    {
        using var context = _contextFactory.CreateDbContext();

        // Creates the database and schema from the model if it doesn't exist.
        // For existing databases, this is a no-op — legacy migration handles the rest.
        context.Database.EnsureCreated();
        _logger.LogInformation("Move history database initialized");
    }


    public static string ResolveDatabasePath(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            configuredPath = "data/move-history.db";
        }

        var fullPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(configuredPath, AppContext.BaseDirectory);

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return fullPath;
    }
}
