using System.Net.Http.Json;
using Starter.Shared;

namespace Starter.Web;

public sealed class DevTodoApiClient(HttpClient httpClient)
{
    public async Task<IReadOnlyList<DevTodoItemDto>> GetTodosAsync(
        string? search = null,
        DevTodoStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query.Add($"search={Uri.EscapeDataString(search.Trim())}");
        }

        if (status is not null)
        {
            query.Add($"status={Uri.EscapeDataString(status.Value.ToString())}");
        }

        var path = query.Count == 0
            ? "/dev/todos"
            : $"/dev/todos?{string.Join("&", query)}";

        return await httpClient.GetFromJsonAsync<List<DevTodoItemDto>>(path, cancellationToken) ?? [];
    }

    public async Task<DevTodoItemDto> CreateAsync(
        DevTodoSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/dev/todos", request, cancellationToken);
        return await ReadResponseAsync<DevTodoItemDto>(response, cancellationToken);
    }

    public async Task<DevTodoItemDto> UpdateAsync(
        Guid id,
        DevTodoSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PutAsJsonAsync($"/dev/todos/{id}", request, cancellationToken);
        return await ReadResponseAsync<DevTodoItemDto>(response, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync($"/dev/todos/{id}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadErrorMessageAsync(response, cancellationToken));
        }
    }

    private static async Task<T> ReadResponseAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                ?? throw new InvalidOperationException("The API returned an empty response.");
        }

        throw new InvalidOperationException(await ReadErrorMessageAsync(response, cancellationToken));
    }

    private static async Task<string> ReadErrorMessageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var error = await response.Content.ReadFromJsonAsync<DevTodoErrorDto>(cancellationToken);
        return error?.Message ?? $"Request failed with HTTP {(int)response.StatusCode}.";
    }
}
