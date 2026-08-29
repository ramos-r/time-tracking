using Microsoft.EntityFrameworkCore;
using TimeTracking.Models;
using DomainTask = TimeTracking.Models.Task;

namespace TimeTracking.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<DomainTask> Tasks => Set<DomainTask>();
    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tag>(entity =>
        {
            entity.Property(t => t.Name).IsRequired().HasMaxLength(100);
            entity.Property(t => t.Color).IsRequired();
        });

        modelBuilder.Entity<DomainTask>(entity =>
        {
            entity.Property(t => t.Name).IsRequired().HasMaxLength(200);

            // Excluir uma Tag não deve excluir as Tasks associadas (Seção 9).
            entity.HasOne(t => t.Tag)
                  .WithMany(tag => tag.Tasks)
                  .HasForeignKey(t => t.TagId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TimeEntry>(entity =>
        {
            // Excluir uma Task exclui suas TimeEntry associadas (cascade, Seção 9).
            entity.HasOne(te => te.Task)
                  .WithMany(t => t.TimeEntries)
                  .HasForeignKey(te => te.TaskId)
                  .OnDelete(DeleteBehavior.Cascade)
                  .IsRequired();
        });
    }
}
