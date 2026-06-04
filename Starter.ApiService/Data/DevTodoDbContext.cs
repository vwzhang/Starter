using Microsoft.EntityFrameworkCore;

namespace Starter.ApiService.Data;

public sealed class DevTodoDbContext(DbContextOptions<DevTodoDbContext> options) : DbContext(options)
{
    public DbSet<DevTodoItem> DevTodoItems => Set<DevTodoItem>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<DevTodoItem>(entity =>
        {
            entity.ToTable("DevTodoItems");
            entity.HasIndex(item => item.Status);
            entity.HasIndex(item => item.DueDate);
            entity.Property(item => item.Title).HasMaxLength(140);
            entity.Property(item => item.Notes).HasMaxLength(1000);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(40);
        });
    }
}
