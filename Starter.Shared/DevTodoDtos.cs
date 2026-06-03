namespace Starter.Shared;

public enum DevTodoStatus
{
    Backlog = 0,
    InProgress = 1,
    Done = 2,
}

public sealed record DevTodoItemDto(
    Guid Id,
    string Title,
    string? Notes,
    DevTodoStatus Status,
    DateOnly? DueDate,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record DevTodoSaveRequest(
    string Title,
    string? Notes,
    DevTodoStatus Status,
    DateOnly? DueDate,
    int SortOrder);

public sealed record DevTodoErrorDto(string Message);
