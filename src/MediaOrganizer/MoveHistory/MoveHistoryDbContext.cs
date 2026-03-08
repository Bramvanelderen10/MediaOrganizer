using Microsoft.EntityFrameworkCore;

namespace MediaOrganizer.MoveHistory;

public class MoveHistoryDbContext : DbContext
{
    public MoveHistoryDbContext(DbContextOptions<MoveHistoryDbContext> options)
        : base(options) { }

    public DbSet<MoveHistoryEntry> MoveHistory => Set<MoveHistoryEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MoveHistoryEntry>(entity =>
        {
            entity.ToTable("MoveHistory");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.UniqueKey).IsRequired();
            entity.Property(e => e.OriginalFilePath).IsRequired();
            entity.Property(e => e.TargetFilePath).IsRequired();
            entity.Property(e => e.MoveDateTime).HasDefaultValue(DateTime.UtcNow);
            entity.Property(e => e.IsMoved).HasDefaultValue(false);

            // Indexes for efficient lookups
            entity.HasIndex(e => e.UniqueKey).IsUnique(false);
            entity.HasIndex(e => e.IsMoved);
            entity.HasIndex(e => new { e.UniqueKey, e.Id }).IsDescending(false, true);
        });
    }
}
