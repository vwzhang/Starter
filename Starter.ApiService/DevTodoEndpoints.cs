using Npgsql;
using NpgsqlTypes;
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
        NpgsqlDataSource dataSource,
        string? search = null,
        DevTodoStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT "Id", "Title", "Notes", "Status", "DueDate", "SortOrder", "CreatedAt", "UpdatedAt"
            FROM "DevTodoItems"
            WHERE (@search IS NULL OR "Title" ILIKE @search OR COALESCE("Notes", '') ILIKE @search)
              AND (@status IS NULL OR "Status" = @status)
            ORDER BY "SortOrder", "CreatedAt" DESC;
            """);

        command.Parameters.Add("search", NpgsqlDbType.Text).Value = string.IsNullOrWhiteSpace(search)
            ? DBNull.Value
            : $"%{search.Trim()}%";
        command.Parameters.Add("status", NpgsqlDbType.Text).Value = status is null
            ? DBNull.Value
            : status.Value.ToString();

        var items = new List<DevTodoItemDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadTodo(reader));
        }

        return Results.Ok(items);
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        NpgsqlDataSource dataSource,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT "Id", "Title", "Notes", "Status", "DueDate", "SortOrder", "CreatedAt", "UpdatedAt"
            FROM "DevTodoItems"
            WHERE "Id" = @id;
            """);
        command.Parameters.Add("id", NpgsqlDbType.Uuid).Value = id;

        var item = await ReadSingleAsync(command, cancellationToken);
        return item is null ? Results.NotFound() : Results.Ok(item);
    }

    private static async Task<IResult> CreateAsync(
        DevTodoSaveRequest request,
        NpgsqlDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var validationMessage = Validate(request);

        if (validationMessage is not null)
        {
            return Results.BadRequest(new DevTodoErrorDto(validationMessage));
        }

        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO "DevTodoItems" ("Id", "Title", "Notes", "Status", "DueDate", "SortOrder", "CreatedAt", "UpdatedAt")
            VALUES (@id, @title, @notes, @status, @dueDate, @sortOrder, @createdAt, @updatedAt)
            RETURNING "Id", "Title", "Notes", "Status", "DueDate", "SortOrder", "CreatedAt", "UpdatedAt";
            """);
        AddUpsertParameters(command, request, now);
        command.Parameters.Add("id", NpgsqlDbType.Uuid).Value = id;
        command.Parameters.Add("createdAt", NpgsqlDbType.TimestampTz).Value = now;

        var item = await ReadSingleAsync(command, cancellationToken)
            ?? throw new InvalidOperationException("The created item was not returned.");

        return Results.Created($"/dev/todos/{item.Id}", item);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        DevTodoSaveRequest request,
        NpgsqlDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var validationMessage = Validate(request);

        if (validationMessage is not null)
        {
            return Results.BadRequest(new DevTodoErrorDto(validationMessage));
        }

        await using var command = dataSource.CreateCommand(
            """
            UPDATE "DevTodoItems"
            SET "Title" = @title,
                "Notes" = @notes,
                "Status" = @status,
                "DueDate" = @dueDate,
                "SortOrder" = @sortOrder,
                "UpdatedAt" = @updatedAt
            WHERE "Id" = @id
            RETURNING "Id", "Title", "Notes", "Status", "DueDate", "SortOrder", "CreatedAt", "UpdatedAt";
            """);
        AddUpsertParameters(command, request, DateTimeOffset.UtcNow);
        command.Parameters.Add("id", NpgsqlDbType.Uuid).Value = id;

        var item = await ReadSingleAsync(command, cancellationToken);
        return item is null ? Results.NotFound() : Results.Ok(item);
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        NpgsqlDataSource dataSource,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            DELETE FROM "DevTodoItems"
            WHERE "Id" = @id;
            """);
        command.Parameters.Add("id", NpgsqlDbType.Uuid).Value = id;

        var deleted = await command.ExecuteNonQueryAsync(cancellationToken);
        return deleted == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static void AddUpsertParameters(NpgsqlCommand command, DevTodoSaveRequest request, DateTimeOffset updatedAt)
    {
        command.Parameters.Add("title", NpgsqlDbType.Text).Value = request.Title.Trim();
        command.Parameters.Add("notes", NpgsqlDbType.Text).Value = string.IsNullOrWhiteSpace(request.Notes)
            ? DBNull.Value
            : request.Notes.Trim();
        command.Parameters.Add("status", NpgsqlDbType.Text).Value = request.Status.ToString();
        command.Parameters.Add("dueDate", NpgsqlDbType.Date).Value = request.DueDate is null
            ? DBNull.Value
            : request.DueDate.Value;
        command.Parameters.Add("sortOrder", NpgsqlDbType.Integer).Value = Math.Max(0, request.SortOrder);
        command.Parameters.Add("updatedAt", NpgsqlDbType.TimestampTz).Value = updatedAt;
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

    private static async Task<DevTodoItemDto?> ReadSingleAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadTodo(reader) : null;
    }

    private static DevTodoItemDto ReadTodo(NpgsqlDataReader reader)
    {
        var statusText = reader.GetString(3);
        var status = Enum.TryParse<DevTodoStatus>(statusText, out var parsedStatus)
            ? parsedStatus
            : DevTodoStatus.Backlog;

        return new DevTodoItemDto(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            status,
            reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4),
            reader.GetInt32(5),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetFieldValue<DateTimeOffset>(7));
    }
}
