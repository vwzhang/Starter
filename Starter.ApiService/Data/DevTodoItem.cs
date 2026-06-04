using Starter.Shared;

namespace Starter.ApiService.Data;

public sealed class DevTodoItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DevTodoStatus Status { get; set; } = DevTodoStatus.Backlog;
    public DateOnly? DueDate { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
