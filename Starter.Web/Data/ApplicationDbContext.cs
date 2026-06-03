using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Starter.Web.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, string>(options)
{
    public DbSet<ApplicationFeature> Features => Set<ApplicationFeature>();
    public DbSet<ApplicationPermission> Permissions => Set<ApplicationPermission>();
    public DbSet<ApplicationRolePermission> RolePermissions => Set<ApplicationRolePermission>();
    public DbSet<ApplicationSetting> Settings => Set<ApplicationSetting>();
    public DbSet<DevTodoItem> DevTodoItems => Set<DevTodoItem>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(user => user.DisplayName).HasMaxLength(200);
            entity.Property(user => user.IsActive).HasDefaultValue(true);
        });

        builder.Entity<ApplicationRole>(entity =>
        {
            entity.Property(role => role.Description).HasMaxLength(512);
        });

        builder.Entity<ApplicationFeature>(entity =>
        {
            entity.ToTable("Features");
            entity.HasIndex(feature => feature.Key).IsUnique();
            entity.Property(feature => feature.Key).HasMaxLength(120);
            entity.Property(feature => feature.Name).HasMaxLength(200);
            entity.Property(feature => feature.Description).HasMaxLength(512);
        });

        builder.Entity<ApplicationPermission>(entity =>
        {
            entity.ToTable("Permissions");
            entity.HasIndex(permission => permission.Key).IsUnique();
            entity.Property(permission => permission.Key).HasMaxLength(160);
            entity.Property(permission => permission.Name).HasMaxLength(200);
            entity.Property(permission => permission.Description).HasMaxLength(512);
            entity.HasOne(permission => permission.Feature)
                .WithMany(feature => feature.Permissions)
                .HasForeignKey(permission => permission.FeatureId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ApplicationRolePermission>(entity =>
        {
            entity.ToTable("RolePermissions");
            entity.HasKey(rolePermission => new { rolePermission.RoleId, rolePermission.PermissionId });
            entity.HasOne(rolePermission => rolePermission.Role)
                .WithMany(role => role.RolePermissions)
                .HasForeignKey(rolePermission => rolePermission.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(rolePermission => rolePermission.Permission)
                .WithMany(permission => permission.RolePermissions)
                .HasForeignKey(rolePermission => rolePermission.PermissionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ApplicationSetting>(entity =>
        {
            entity.ToTable("Settings");
            entity.HasIndex(setting => setting.Key).IsUnique();
            entity.Property(setting => setting.Key).HasMaxLength(160);
            entity.Property(setting => setting.Name).HasMaxLength(200);
            entity.Property(setting => setting.Category).HasMaxLength(120);
            entity.Property(setting => setting.Value).HasMaxLength(2048);
            entity.Property(setting => setting.DefaultValue).HasMaxLength(2048);
            entity.Property(setting => setting.ValueType).HasMaxLength(40);
            entity.Property(setting => setting.Description).HasMaxLength(512);
        });

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
