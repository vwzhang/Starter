using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Starter.Shared;

namespace Starter.Web.Services;

public sealed class AiChatService(
    HttpClient httpClient,
    SystemConfigurationService systemConfiguration,
    ILogger<AiChatService> logger)
{
    public async Task<AiChatStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var settings = await systemConfiguration.GetAiApiSettingsAsync();
        var provider = settings.CurrentProviderSettings;

        if (provider is null)
        {
            return new AiChatStatusResponse(
                false,
                null,
                null,
                null,
                $"Unknown AI provider '{settings.CurrentProvider}'. Select ChatGPT, Gemini, GitHub Models, Groq, or Azure Foundry in Admin settings.");
        }

        return new AiChatStatusResponse(
            provider.IsConfigured,
            string.IsNullOrWhiteSpace(provider.Endpoint) ? null : provider.Endpoint,
            string.IsNullOrWhiteSpace(provider.Model) ? null : provider.Model,
            provider.DisplayName,
            provider.IsConfigured
                ? $"{provider.DisplayName} is selected for AI chat."
                : $"{provider.DisplayName} needs endpoint, model, and API key settings.");
    }

    public async Task<AiChatResponse> ChatAsync(
        AiChatRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            throw new InvalidOperationException("Message is required.");
        }

        var settings = await systemConfiguration.GetAiApiSettingsAsync();
        var provider = settings.CurrentProviderSettings
            ?? throw new InvalidOperationException($"Unknown AI provider '{settings.CurrentProvider}'.");

        if (!provider.IsConfigured)
        {
            throw new InvalidOperationException($"{provider.DisplayName} is not configured. Set endpoint, model, and API key in Admin settings.");
        }

        var endpoint = NormalizeChatEndpoint(provider);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        ApplyAuthentication(httpRequest, provider);
        ApplyProviderHeaders(httpRequest, provider.Key);
        httpRequest.Content = IsResponsesEndpoint(endpoint)
            ? JsonContent.Create(new
            {
                model = provider.Model,
                input = new object[]
                {
                    new
                    {
                        role = "system",
                        content = string.IsNullOrWhiteSpace(settings.SystemPrompt)
                            ? "You are a helpful assistant."
                            : settings.SystemPrompt,
                    },
                    new
                    {
                        role = "user",
                        content = request.Message.Trim(),
                    },
                },
            })
            : JsonContent.Create(new
        {
            model = provider.Model,
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = string.IsNullOrWhiteSpace(settings.SystemPrompt)
                        ? "You are a helpful assistant."
                        : settings.SystemPrompt,
                },
                new
                {
                    role = "user",
                    content = request.Message.Trim(),
                },
            },
        });

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var message = TryReadErrorMessage(responseText)
                ?? $"AI provider returned {(int)response.StatusCode} {response.ReasonPhrase}.";
            message = GetProviderErrorMessage(provider.Key, provider.Model, message);

            logger.LogWarning(
                "AI provider {Provider} returned HTTP {StatusCode}: {Message}",
                provider.Key,
                (int)response.StatusCode,
                message);

            throw new InvalidOperationException(message);
        }

        var answer = IsResponsesEndpoint(endpoint)
            ? TryReadResponsesMessage(responseText)
            : TryReadAssistantMessage(responseText);

        if (string.IsNullOrWhiteSpace(answer))
        {
            throw new InvalidOperationException("AI provider did not return a supported chat response. Check that the configured endpoint is a chat completions or responses endpoint, not a models or catalog endpoint.");
        }

        return new AiChatResponse(answer, provider.Model, provider.DisplayName);
    }

    private static string? TryReadAssistantMessage(string responseText)
    {
        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!root.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                return null;
            }

            var firstChoice = choices[0];
            if (!firstChoice.TryGetProperty("message", out var message)
                || !message.TryGetProperty("content", out var content))
            {
                return null;
            }

            return content.GetString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryReadResponsesMessage(string responseText)
    {
        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (root.TryGetProperty("output_text", out var outputText)
                && outputText.ValueKind == JsonValueKind.String)
            {
                return outputText.GetString();
            }

            if (!root.TryGetProperty("output", out var output)
                || output.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content)
                    || content.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var contentItem in content.EnumerateArray())
                {
                    if (contentItem.TryGetProperty("text", out var text)
                        && text.ValueKind == JsonValueKind.String)
                    {
                        return text.GetString();
                    }
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Uri NormalizeChatEndpoint(AiApiProviderSettings provider)
    {
        var endpoint = provider.Endpoint.Trim();

        if (provider.Key != AiApiProviderKeys.AzureFoundry)
        {
            return new Uri(endpoint, UriKind.Absolute);
        }

        var trimmedEndpoint = endpoint.TrimEnd('/');

        if (trimmedEndpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            || trimmedEndpoint.EndsWith("/responses", StringComparison.OrdinalIgnoreCase)
            || trimmedEndpoint.Contains("/deployments/", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(endpoint, UriKind.Absolute);
        }

        return new Uri(trimmedEndpoint + "/chat/completions", UriKind.Absolute);
    }

    private static bool IsResponsesEndpoint(Uri endpoint)
    {
        return endpoint.AbsolutePath.EndsWith("/responses", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyAuthentication(HttpRequestMessage request, AiApiProviderSettings provider)
    {
        if (provider.Key == AiApiProviderKeys.AzureFoundry)
        {
            request.Headers.TryAddWithoutValidation("api-key", provider.ApiKey);
            return;
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
    }

    private static void ApplyProviderHeaders(HttpRequestMessage request, string providerKey)
    {
        if (providerKey != AiApiProviderKeys.GitHub)
        {
            return;
        }

        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
    }

    private static string GetProviderErrorMessage(string providerKey, string model, string message)
    {
        if (providerKey == AiApiProviderKeys.GitHub
            && message.Contains("No access to model", StringComparison.OrdinalIgnoreCase))
        {
            return $"{message} GitHub Models catalog access can succeed even when inference access to a specific model is blocked. Try selecting 'openai/gpt-4.1' in /admin/ai, or confirm the token has Models: Read permission and the model is enabled for your GitHub account or organization.";
        }

        return message;
    }

    private static string? TryReadErrorMessage(string responseText)
    {
        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

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
}
