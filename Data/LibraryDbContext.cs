using Microsoft.EntityFrameworkCore;
using OnLibrary.Models;

namespace OnLibrary.Data;

public sealed class LibraryDbContext(DbContextOptions<LibraryDbContext> options) : DbContext(options)
{
    public DbSet<LibraryDocument> Documents => Set<LibraryDocument>();
    public DbSet<LibraryUser> Users => Set<LibraryUser>();
    public DbSet<LibraryFavorite> Favorites => Set<LibraryFavorite>();
    public DbSet<LibraryReview> Reviews => Set<LibraryReview>();
    public DbSet<ReadingHistory> ReadingHistory => Set<ReadingHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LibraryDocument>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Title).HasMaxLength(120).IsRequired();
            entity.Property(d => d.Author).HasMaxLength(100).IsRequired();
            entity.Property(d => d.Category).HasMaxLength(60).IsRequired();
            entity.Property(d => d.Format).HasMaxLength(10).IsRequired();
            entity.Property(d => d.Color).HasMaxLength(20).IsRequired();
            entity.Property(d => d.Initials).HasMaxLength(8).IsRequired();
            entity.Property(d => d.Description).HasMaxLength(1000).IsRequired();
            entity.Property(d => d.StoredFileName).HasMaxLength(80);
            entity.Property(d => d.FileMimeType).HasMaxLength(100);
        });
        modelBuilder.Entity<LibraryUser>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.HasIndex(u => u.Email).IsUnique();
            entity.Property(u => u.Email).HasMaxLength(254).IsRequired();
            entity.Property(u => u.DisplayName).HasMaxLength(80).IsRequired();
            entity.Property(u => u.PasswordHash).HasMaxLength(500).IsRequired();
            entity.Property(u => u.Role).HasMaxLength(20).IsRequired();
        });
        modelBuilder.Entity<LibraryFavorite>(entity =>
        {
            entity.HasKey(f => new { f.UserId, f.DocumentId });
            entity.HasIndex(f => f.DocumentId);
        });
        modelBuilder.Entity<LibraryReview>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.HasIndex(r => new { r.UserId, r.DocumentId }).IsUnique();
            entity.Property(r => r.Comment).HasMaxLength(1000).IsRequired();
            entity.HasIndex(r => r.DocumentId);
        });
        modelBuilder.Entity<ReadingHistory>(entity =>
        {
            entity.HasKey(h => h.Id);
            entity.HasIndex(h => new { h.UserId, h.DocumentId }).IsUnique();
            entity.HasIndex(h => new { h.UserId, h.LastReadAt });
        });
    }
}
