using System.Net.Http.Headers;
using System.Text.Json;

namespace Starter.Web.Services;

public sealed class AiModelCatalogService(
    HttpClient httpClient,
    SystemConfigurationService systemConfiguration,
    ILogger<AiModelCatalogService> logger)
{
    public async Task<IReadOnlyList<AiModelSummary>> GetModelsAsync(
        AiModelCatalogRequest request,
        CancellationToken cancellationToken = default)
    {
        var provider = await ResolveProviderAsync(request);

        if (string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            throw new InvalidOperationException($"{provider.DisplayName} API key is required to load models.");
        }

        var modelsEndpoint = GetModelsEndpoint(provider);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, modelsEndpoint);
        ApplyAuthentication(httpRequest, provider);

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var message = TryReadErrorMessage(responseText)
                ?? $"Model catalog returned {(int)response.StatusCode} {response.ReasonPhrase}.";

            logger.LogWarning(
                "AI provider {Provider} model catalog returned HTTP {StatusCode}: {Message}",
                provider.Key,
                (int)response.StatusCode,
                message);

            throw new InvalidOperationException(message);
        }

        return ParseModels(provider.Key, responseText)
            .OrderBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<ResolvedAiProvider> ResolveProviderAsync(AiModelCatalogRequest request)
    {
        var settings = await systemConfiguration.GetAiApiSettingsAsync();
        var savedProvider = settings.Providers.FirstOrDefault(provider =>
            string.Equals(provider.Key, request.ProviderKey, StringComparison.OrdinalIgnoreCase));

        if (savedProvider is null)
        {
            throw new InvalidOperationException($"Unknown AI provider '{request.ProviderKey}'.");
        }

        return new ResolvedAiProvider(
            savedProvider.Key,
            savedProvider.DisplayName,
            string.IsNullOrWhiteSpace(request.Endpoint) ? savedProvider.Endpoint : request.Endpoint.Trim(),
            string.IsNullOrWhiteSpace(request.ApiKey) ? savedProvider.ApiKey : request.ApiKey);
    }

    private static void ApplyAuthentication(HttpRequestMessage request, ResolvedAiProvider provider)
    {
        if (provider.Key == AiApiProviderKeys.Gemini)
        {
            return;
        }

        if (provider.Key == AiApiProviderKeys.AzureFoundry)
        {
            request.Headers.TryAddWithoutValidation("api-key", provider.ApiKey);
            return;
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);

        if (provider.Key == AiApiProviderKeys.GitHub)
        {
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        }
    }

    private static Uri GetModelsEndpoint(ResolvedAiProvider provider)
    {
        if (string.IsNullOrWhiteSpace(provider.Endpoint))
        {
            throw new InvalidOperationException("Endpoint is required to load models.");
        }

        var endpoint = new Uri(provider.Endpoint, UriKind.Absolute);

        if (provider.Key == AiApiProviderKeys.GitHub)
        {
            return new Uri(endpoint.GetLeftPart(UriPartial.Authority) + "/catalog/models");
        }

        if (provider.Key == AiApiProviderKeys.Gemini)
        {
            return new Uri("https://generativelanguage.googleapis.com/v1beta/models?key=" + Uri.EscapeDataString(provider.ApiKey));
        }

        if (provider.Key == AiApiProviderKeys.AzureFoundry)
        {
            var azurePath = endpoint.AbsolutePath.TrimEnd('/');

            if (azurePath.EndsWith("/responses", StringComparison.OrdinalIgnoreCase))
            {
                return new Uri(endpoint.GetLeftPart(UriPartial.Authority) + azurePath[..^"/responses".Length] + "/models");
            }

            if (azurePath.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                return new Uri(endpoint.GetLeftPart(UriPartial.Authority) + azurePath[..^"/chat/completions".Length] + "/models");
            }

            if (azurePath.Contains("/deployments/", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Azure deployment-specific endpoints do not expose a model list. Enter the deployment name manually in the Model field.");
            }

            return new Uri(endpoint.GetLeftPart(UriPartial.Authority) + azurePath + "/models");
        }

        var path = endpoint.AbsolutePath.TrimEnd('/');
        var modelsPath = path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? path[..^"/chat/completions".Length] + "/models"
            : "/v1/models";

        return new Uri(endpoint.GetLeftPart(UriPartial.Authority) + modelsPath);
    }

    private static IReadOnlyList<AiModelSummary> ParseModels(string providerKey, string responseText)
    {
        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;

        if (providerKey == AiApiProviderKeys.Gemini)
        {
            return ParseGeminiModels(root);
        }

        if (root.ValueKind == JsonValueKind.Array)
        {
            return ParseModelArray(root);
        }

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            return ParseModelArray(data);
        }

        if (root.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
        {
            return ParseModelArray(models);
        }

        return [];
    }

    private static IReadOnlyList<AiModelSummary> ParseGeminiModels(JsonElement root)
    {
        if (!root.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<AiModelSummary>();

        foreach (var model in models.EnumerateArray())
        {
            if (model.TryGetProperty("supportedGenerationMethods", out var methods)
                && methods.ValueKind == JsonValueKind.Array
                && !methods.EnumerateArray().Any(method =>
                    string.Equals(method.GetString(), "generateContent", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var id = TryReadString(model, "name")?.Replace("models/", string.Empty, StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(id))
            {
                result.Add(new AiModelSummary(id, TryReadString(model, "displayName")));
            }
        }

        return result;
    }

    private static IReadOnlyList<AiModelSummary> ParseModelArray(JsonElement models)
    {
        var result = new List<AiModelSummary>();

        foreach (var model in models.EnumerateArray())
        {
            var id = TryReadString(model, "id")
                ?? TryReadString(model, "name")
                ?? TryReadString(model, "model");

            if (!string.IsNullOrWhiteSpace(id))
            {
                result.Add(new AiModelSummary(id, TryReadString(model, "displayName") ?? TryReadString(model, "name")));
            }
        }

        return result;
    }

    private static string? TryReadErrorMessage(string responseText)
    {
        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;

            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("message", out var objectMessage))
                {
                    return objectMessage.GetString();
                }

                if (error.ValueKind == JsonValueKind.String)
                {
                    return error.GetString();
                }
            }

            return root.TryGetProperty("message", out var message)
                ? message.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private sealed record ResolvedAiProvider(
        string Key,
        string DisplayName,
        string Endpoint,
        string ApiKey);
}

public sealed record AiModelCatalogRequest(
    string ProviderKey,
    string Endpoint,
    string ApiKey);

public sealed record AiModelSummary(
    string Id,
    string? DisplayName);
