using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Starter.ApiService.Data;

public sealed class DevTodoDbContextFactory : IDesignTimeDbContextFactory<DevTodoDbContext>
{
    public DevTodoDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__starterdb")
            ?? "Host=localhost;Port=5432;Database=starterdb;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<DevTodoDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new DevTodoDbContext(options);
    }
}
