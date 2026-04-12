using Microsoft.EntityFrameworkCore;
using BapalaServer.Models;

namespace BapalaServer.Data;

public class BapalaDbContext(DbContextOptions<BapalaDbContext> options) : DbContext(options)
{
    public DbSet<MediaItem> MediaItems => Set<MediaItem>();
    public DbSet<WatchHistory> WatchHistory => Set<WatchHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<MediaItem>(e =>
        {
            e.HasIndex(m => m.Title);
            e.HasIndex(m => m.Type);
            e.HasIndex(m => m.Year);
            e.HasIndex(m => m.IsFavorite);
        });

        modelBuilder.Entity<WatchHistory>(e =>
        {
            e.HasOne(w => w.MediaItem)
             .WithMany(m => m.WatchHistory)
             .HasForeignKey(w => w.MediaItemId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(w => new { w.MediaItemId, w.WatchedAt });
        });
    }
}
