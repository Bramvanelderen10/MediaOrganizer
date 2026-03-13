using Microsoft.EntityFrameworkCore;

namespace MediaOrganizer.Settings;

public class SettingEntriesDbContext : DbContext
{
    public SettingEntriesDbContext(DbContextOptions<SettingEntriesDbContext> options)
        : base(options) { }

    public DbSet<SettingEntry> Settings => Set<SettingEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SettingEntry>(entity =>
        {
            entity.ToTable("Settings");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.Key).IsRequired().HasConversion<string>();
            entity.Property(e => e.Value).IsRequired();

            // Indexes for efficient lookups
            entity.HasIndex(e => e.Key).IsUnique(true);
        });
    }
}
