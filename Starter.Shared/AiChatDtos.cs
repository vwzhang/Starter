namespace Starter.Shared;

public sealed record AiChatStatusResponse(
    bool IsConfigured,
    string? Endpoint,
    string? Model,
    string? Provider,
    string? Message);

public sealed record AiChatRequest(
    string Message,
    string? ProviderKey = null,
    IReadOnlyList<AiChatMessage>? Conversation = null);

public sealed record AiChatMessage(string Role, string Message);

public sealed record AiChatResponse(
    string Message,
    string Model,
    string Provider);
