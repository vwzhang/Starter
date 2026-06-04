using Microsoft.EntityFrameworkCore;
using Starter.ApiService.Data;
using Starter.Shared;

namespace Starter.ApiService;

internal static class DevTodoEndpoints
{
    public static IEndpointRouteBuilder MapDevTodoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/dev/todos")
            .WithTags("Dev");

        group.MapGet("", ListAsync)
            .WithName("ListDevTodos");

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetDevTodo");

        group.MapPost("", CreateAsync)
            .WithName("CreateDevTodo");

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateDevTodo");

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteDevTodo");

        return app;
    }

    private static async Task<IResult> ListAsync(
        DevTodoDbContext dbContext,
        string? search = null,
        DevTodoStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.DevTodoItems.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(item =>
                EF.Functions.Like(item.Title, pattern)
                || (item.Notes != null && EF.Functions.Like(item.Notes, pattern)));
        }

        if (status is not null)
        {
            query = query.Where(item => item.Status == status.Value);
        }

        var items = await query
            .OrderBy(item => item.SortOrder)
            .ThenByDescending(item => item.CreatedAt)
            .Select(item => ToDto(item))
            .ToListAsync(cancellationToken);

        return Results.Ok(items);
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        DevTodoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var item = await dbContext.DevTodoItems
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => ToDto(item))
            .SingleOrDefaultAsync(cancellationToken);

        return item is null ? Results.NotFound() : Results.Ok(item);
    }

    private static async Task<IResult> CreateAsync(
        DevTodoSaveRequest request,
        DevTodoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var validationMessage = Validate(request);

        if (validationMessage is not null)
        {
            return Results.BadRequest(new DevTodoErrorDto(validationMessage));
        }

        var now = DateTimeOffset.UtcNow;
        var item = new DevTodoItem
        {
            Id = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now,
        };

        ApplyRequest(item, request, now);

        dbContext.DevTodoItems.Add(item);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Created($"/dev/todos/{item.Id}", ToDto(item));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        DevTodoSaveRequest request,
        DevTodoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var validationMessage = Validate(request);

        if (validationMessage is not null)
        {
            return Results.BadRequest(new DevTodoErrorDto(validationMessage));
        }

        var item = await dbContext.DevTodoItems.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (item is null)
        {
            return Results.NotFound();
        }

        ApplyRequest(item, request, DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToDto(item));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        DevTodoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var deleted = await dbContext.DevTodoItems
            .Where(item => item.Id == id)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static void ApplyRequest(DevTodoItem item, DevTodoSaveRequest request, DateTimeOffset updatedAt)
    {
        item.Title = request.Title.Trim();
        item.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        item.Status = request.Status;
        item.DueDate = request.DueDate;
        item.SortOrder = Math.Max(0, request.SortOrder);
        item.UpdatedAt = updatedAt;
    }

    private static string? Validate(DevTodoSaveRequest request)
    {
        var title = request.Title.Trim();
        var notes = request.Notes?.Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            return "Title is required.";
        }

        if (title.Length > 140)
        {
            return "Title must be 140 characters or fewer.";
        }

        if (notes?.Length > 1000)
        {
            return "Notes must be 1000 characters or fewer.";
        }

        return Enum.IsDefined(request.Status)
            ? null
            : "Status is invalid.";
    }

    private static DevTodoItemDto ToDto(DevTodoItem item)
    {
        return new DevTodoItemDto(
            item.Id,
            item.Title,
            item.Notes,
            item.Status,
            item.DueDate,
            item.SortOrder,
            item.CreatedAt,
            item.UpdatedAt);
    }
}
