using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Starter.Shared;

namespace Starter.Web.Services;

public sealed class AiChatService(
    HttpClient httpClient,
    SystemConfigurationService systemConfiguration,
    ILogger<AiChatService> logger)
{
    public async Task<AiChatStatusResponse> GetStatusAsync(
        string? providerKey = null,
        CancellationToken cancellationToken = default)
    {
        var settings = await systemConfiguration.GetAiApiSettingsAsync();
        var selectedProviderKey = string.IsNullOrWhiteSpace(providerKey)
            ? settings.CurrentProvider
            : providerKey.Trim();
        var provider = GetProvider(settings, selectedProviderKey);

        if (provider is null)
        {
            return new AiChatStatusResponse(
                false,
                null,
                null,
                null,
                $"Unknown AI provider '{selectedProviderKey}'. Select ChatGPT, DeepSeek, Gemini, GitHub Models, Groq, or Azure Foundry in Admin settings.");
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
        var provider = GetProvider(settings, request.ProviderKey ?? settings.CurrentProvider)
            ?? throw new InvalidOperationException($"Unknown AI provider '{request.ProviderKey ?? settings.CurrentProvider}'.");

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
                input = ToChatMessages(request, settings.SystemPrompt),
            })
            : JsonContent.Create(new
        {
            model = provider.Model,
            messages = ToChatMessages(request, settings.SystemPrompt),
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

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var settings = await systemConfiguration.GetAiApiSettingsAsync();
        var provider = settings.CurrentProviderSettings
            ?? throw new InvalidOperationException($"Unknown AI provider '{settings.CurrentProvider}'.");

        if (!provider.IsConfigured)
        {
            throw new InvalidOperationException($"{provider.DisplayName} is not configured. Set endpoint, model, and API key in Admin settings.");
        }

        var endpoint = NormalizeChatEndpoint(provider);
        if (IsResponsesEndpoint(endpoint) && options?.Tools?.Count > 0)
        {
            throw new InvalidOperationException("Agents.AI tool calling currently requires a chat completions compatible endpoint. Configure /admin/ai to use a /chat/completions endpoint.");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        ApplyAuthentication(httpRequest, provider);
        ApplyProviderHeaders(httpRequest, provider.Key);

        var payload = new Dictionary<string, object?>
        {
            ["model"] = provider.Model,
            ["messages"] = ToOpenAiMessages(chatMessages, settings.SystemPrompt),
        };

        var tools = ToOpenAiTools(options?.Tools);
        if (tools.Count > 0)
        {
            payload["tools"] = tools;
            payload["tool_choice"] = "auto";
            payload["parallel_tool_calls"] = options?.AllowMultipleToolCalls ?? true;
        }

        if (options?.Temperature is not null)
        {
            payload["temperature"] = options.Temperature;
        }

        if (options?.MaxOutputTokens is not null)
        {
            payload["max_tokens"] = options.MaxOutputTokens;
        }

        httpRequest.Content = JsonContent.Create(payload);

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

        return ToChatResponse(responseText, provider.Model);
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

    private static List<object> ToChatMessages(AiChatRequest request, string systemPrompt)
    {
        var messages = new List<object>
        {
            new
            {
                role = "system",
                content = string.IsNullOrWhiteSpace(systemPrompt)
                    ? "You are a helpful assistant."
                    : systemPrompt,
            },
        };

        var conversation = request.Conversation?
            .Where(message => !string.IsNullOrWhiteSpace(message.Message)
                && (string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)))
            .TakeLast(20)
            .ToList()
            ?? [];

        foreach (var message in conversation)
        {
            messages.Add(new
            {
                role = message.Role.Trim().ToLowerInvariant(),
                content = message.Message.Trim(),
            });
        }

        if (conversation.Count == 0
            || !string.Equals(conversation[^1].Role, "user", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(conversation[^1].Message.Trim(), request.Message.Trim(), StringComparison.Ordinal))
        {
            messages.Add(new
            {
                role = "user",
                content = request.Message.Trim(),
            });
        }

        return messages;
    }

    private static AiApiProviderSettings? GetProvider(AiApiSettings settings, string providerKey)
    {
        return settings.Providers.FirstOrDefault(provider =>
            string.Equals(provider.Key, providerKey, StringComparison.OrdinalIgnoreCase));
    }

    private static List<Dictionary<string, object?>> ToOpenAiMessages(
        IEnumerable<ChatMessage> chatMessages,
        string systemPrompt)
    {
        var messages = new List<Dictionary<string, object?>>();

        if (!string.IsNullOrWhiteSpace(systemPrompt)
            && !chatMessages.Any(message => message.Role == ChatRole.System))
        {
            messages.Add(new Dictionary<string, object?>
            {
                ["role"] = "system",
                ["content"] = systemPrompt,
            });
        }

        foreach (var message in chatMessages)
        {
            var functionResults = message.Contents.OfType<FunctionResultContent>().ToList();
            if (functionResults.Count > 0)
            {
                foreach (var result in functionResults)
                {
                    messages.Add(new Dictionary<string, object?>
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = result.CallId,
                        ["content"] = result.Result?.ToString() ?? string.Empty,
                    });
                }

                continue;
            }

            var functionCalls = message.Contents.OfType<FunctionCallContent>().ToList();
            var openAiMessage = new Dictionary<string, object?>
            {
                ["role"] = ToOpenAiRole(message.Role),
                ["content"] = string.IsNullOrWhiteSpace(message.Text) ? null : message.Text,
            };

            if (functionCalls.Count > 0)
            {
                openAiMessage["tool_calls"] = functionCalls.Select(call => new
                {
                    id = call.CallId,
                    type = "function",
                    function = new
                    {
                        name = call.Name,
                        arguments = JsonSerializer.Serialize(call.Arguments),
                    },
                }).ToList();
            }

            messages.Add(openAiMessage);
        }

        return messages;
    }

    private static string ToOpenAiRole(ChatRole role)
    {
        if (role == ChatRole.System)
        {
            return "system";
        }

        if (role == ChatRole.Assistant)
        {
            return "assistant";
        }

        return "user";
    }

    private static List<object> ToOpenAiTools(IList<AITool>? tools)
    {
        if (tools is null || tools.Count == 0)
        {
            return [];
        }

        return tools
            .OfType<AIFunctionDeclaration>()
            .Select(tool => (object)new
            {
                type = "function",
                function = new
                {
                    name = tool.Name,
                    description = tool.Description,
                    parameters = tool.JsonSchema,
                },
            })
            .ToList();
    }

    private static ChatResponse ToChatResponse(string responseText, string model)
    {
        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;
        var message = root.GetProperty("choices")[0].GetProperty("message");

        if (message.TryGetProperty("tool_calls", out var toolCalls)
            && toolCalls.ValueKind == JsonValueKind.Array
            && toolCalls.GetArrayLength() > 0)
        {
            var contents = new List<AIContent>();

            foreach (var toolCall in toolCalls.EnumerateArray())
            {
                var id = toolCall.GetProperty("id").GetString() ?? Guid.NewGuid().ToString("N");
                var function = toolCall.GetProperty("function");
                var name = function.GetProperty("name").GetString() ?? string.Empty;
                var argumentsJson = function.TryGetProperty("arguments", out var arguments)
                    ? arguments.GetString() ?? "{}"
                    : "{}";

                contents.Add(new FunctionCallContent(id, name, ParseFunctionArguments(argumentsJson)));
            }

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, contents))
            {
                ModelId = model,
            };
        }

        var content = message.TryGetProperty("content", out var contentElement)
            ? contentElement.GetString()
            : null;

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, content ?? string.Empty))
        {
            ModelId = model,
        };
    }

    private static Dictionary<string, object?> ParseFunctionArguments(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(argumentsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            return document.RootElement
                .EnumerateObject()
                .ToDictionary(property => property.Name, property => ReadJsonValue(property.Value));
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static object? ReadJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number when element.TryGetDouble(out var number) => number,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.Clone(),
        };
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
