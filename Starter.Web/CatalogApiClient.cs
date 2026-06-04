using System.Net.Http.Json;
using Starter.Shared;

namespace Starter.Web;

public sealed class CatalogApiClient(HttpClient httpClient)
{
    public async Task<IReadOnlyList<CatalogCategoryDto>> GetCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<List<CatalogCategoryDto>>(
            "/dev/catalog/categories",
            cancellationToken) ?? [];
    }

    public async Task<CatalogCategoryDto> CreateCategoryAsync(
        CatalogCategorySaveRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/dev/catalog/categories", request, cancellationToken);
        return await ReadResponseAsync<CatalogCategoryDto>(response, cancellationToken);
    }

    public async Task<CatalogCategoryDto> UpdateCategoryAsync(
        Guid id,
        CatalogCategorySaveRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PutAsJsonAsync($"/dev/catalog/categories/{id}", request, cancellationToken);
        return await ReadResponseAsync<CatalogCategoryDto>(response, cancellationToken);
    }

    public async Task DeleteCategoryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync($"/dev/catalog/categories/{id}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<CatalogProductDto>> GetProductsAsync(
        Guid? categoryId = null,
        string? search = null,
        bool? active = null,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>();

        if (categoryId is not null)
        {
            query.Add($"categoryId={Uri.EscapeDataString(categoryId.Value.ToString())}");
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query.Add($"search={Uri.EscapeDataString(search.Trim())}");
        }

        if (active is not null)
        {
            query.Add($"active={Uri.EscapeDataString(active.Value.ToString().ToLowerInvariant())}");
        }

        var path = query.Count == 0
            ? "/dev/catalog/products"
            : $"/dev/catalog/products?{string.Join("&", query)}";

        return await httpClient.GetFromJsonAsync<List<CatalogProductDto>>(path, cancellationToken) ?? [];
    }

    public async Task<CatalogProductDto> CreateProductAsync(
        CatalogProductSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/dev/catalog/products", request, cancellationToken);
        return await ReadResponseAsync<CatalogProductDto>(response, cancellationToken);
    }

    public async Task<CatalogProductDto> UpdateProductAsync(
        Guid id,
        CatalogProductSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PutAsJsonAsync($"/dev/catalog/products/{id}", request, cancellationToken);
        return await ReadResponseAsync<CatalogProductDto>(response, cancellationToken);
    }

    public async Task DeleteProductAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync($"/dev/catalog/products/{id}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
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

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await ReadErrorMessageAsync(response, cancellationToken));
        }
    }

    private static async Task<string> ReadErrorMessageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var error = await response.Content.ReadFromJsonAsync<CatalogErrorDto>(cancellationToken);
        return error?.Message ?? $"Request failed with HTTP {(int)response.StatusCode}.";
    }
}
