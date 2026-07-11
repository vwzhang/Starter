using Microsoft.Extensions.AI;

namespace Starter.Experiment;

public class MockChatClient : IChatClient
{
    public void Dispose()
    {
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var lastMessage = chatMessages.LastOrDefault();
        var lastUserMessage = chatMessages.LastOrDefault(message => message.Role == ChatRole.User)?.Text
            ?? lastMessage?.Text
            ?? "empty prompt";

        var functionResults = chatMessages
            .SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .ToList();

        if (functionResults.Count > 0)
        {
            var summary = string.Join(
                "\n\n",
                functionResults.Select(result => $"Tool result {result.CallId}: {result.Result}"));
            var responseText = $"[Mock Response with Tool]\nI used the available real tools and combined their outputs:\n\n{summary}";

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText))
            {
                ModelId = "local-mock-model",
            });
        }

        var requestedCalls = new List<FunctionCallContent>();

        if (MentionsInternetSearch(lastUserMessage))
        {
            requestedCalls.Add(new FunctionCallContent(
                "call_internet_1",
                "SearchInternet",
                new Dictionary<string, object?>
                {
                    ["query"] = ExtractSearchQuery(lastUserMessage),
                }));
        }

        if (MentionsCatalogSearch(lastUserMessage))
        {
            requestedCalls.Add(new FunctionCallContent(
                "call_catalog_1",
                "SearchLocalCatalog",
                new Dictionary<string, object?>
                {
                    ["query"] = ExtractCatalogQuery(lastUserMessage),
                }));
        }

        if (requestedCalls.Count > 0)
        {
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, requestedCalls.Cast<AIContent>().ToList()))
            {
                ModelId = "local-mock-model",
            });
        }

        if (lastUserMessage.Contains("weather", StringComparison.OrdinalIgnoreCase))
        {
            var callContent = new FunctionCallContent("call_weather_1", "GetCurrentWeather", new Dictionary<string, object?>
            {
                ["location"] = "London",
            });

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [callContent]))
            {
                ModelId = "local-mock-model",
            });
        }

        if (lastUserMessage.Contains("stock", StringComparison.OrdinalIgnoreCase))
        {
            var callContent = new FunctionCallContent("call_stock_1", "GetStockPrice", new Dictionary<string, object?>
            {
                ["ticker"] = "MSFT",
            });

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [callContent]))
            {
                ModelId = "local-mock-model",
            });
        }

        if (lastUserMessage.Contains("[Editor-Input]", StringComparison.OrdinalIgnoreCase))
        {
            var index = lastUserMessage.IndexOf("[Editor-Input]", StringComparison.OrdinalIgnoreCase);
            var draft = lastUserMessage[(index + "[Editor-Input]".Length)..].Trim();
            var responseText = $"[Editor Agent Output]\nTitle: THE EXPANSION OF INTELLIGENCE\n\nPolished Draft:\n{draft.ToUpperInvariant()} (Reviewed and finalized by EditorAgent)";

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText))
            {
                ModelId = "local-editor-model",
            });
        }

        if (lastUserMessage.Contains("space exploration", StringComparison.OrdinalIgnoreCase)
            || lastUserMessage.Contains("topic", StringComparison.OrdinalIgnoreCase))
        {
            var responseText = "[Writer Agent Output] Artificial intelligence will reshape space exploration by helping autonomous probes navigate remote worlds and prioritize discoveries.";

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText))
            {
                ModelId = "local-writer-model",
            });
        }

        var defaultResponse = $"[Mock Response] I received your prompt: \"{lastUserMessage}\". This response was generated locally.";
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, defaultResponse))
        {
            ModelId = "local-mock-model",
        });
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    private static bool MentionsInternetSearch(string message)
    {
        return message.Contains("internet", StringComparison.OrdinalIgnoreCase)
            || message.Contains("web", StringComparison.OrdinalIgnoreCase)
            || message.Contains("search", StringComparison.OrdinalIgnoreCase)
            || message.Contains("online", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MentionsCatalogSearch(string message)
    {
        return message.Contains("catalog", StringComparison.OrdinalIgnoreCase)
            || message.Contains("database", StringComparison.OrdinalIgnoreCase)
            || message.Contains("inventory", StringComparison.OrdinalIgnoreCase)
            || message.Contains("local", StringComparison.OrdinalIgnoreCase)
            || message.Contains("product", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractSearchQuery(string message)
    {
        return message.Contains("Aspire", StringComparison.OrdinalIgnoreCase)
            ? ".NET Aspire latest"
            : message;
    }

    private static string ExtractCatalogQuery(string message)
    {
        if (message.Contains("support", StringComparison.OrdinalIgnoreCase))
        {
            return "support";
        }

        if (message.Contains("laptop", StringComparison.OrdinalIgnoreCase))
        {
            return "laptop";
        }

        return "subscription";
    }
}
