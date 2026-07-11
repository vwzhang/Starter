using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Starter.Web.Data;

namespace Starter.Web.Services;

public sealed class AgentConversationStore(ApplicationDbContext dbContext)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<AgentConversationMessage>> LoadAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var conversation = await dbContext.DevAgentConversations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Key == key, cancellationToken);

        if (conversation is null || string.IsNullOrWhiteSpace(conversation.TranscriptJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<AgentConversationMessage>>(
                conversation.TranscriptJson,
                JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public async Task SaveAsync(
        string key,
        IReadOnlyList<AgentConversationMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var conversation = await dbContext.DevAgentConversations
            .SingleOrDefaultAsync(item => item.Key == key, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var transcriptJson = JsonSerializer.Serialize(messages.TakeLast(20), JsonOptions);

        if (conversation is null)
        {
            dbContext.DevAgentConversations.Add(new DevAgentConversation
            {
                Key = key,
                Title = "Agents.AI Dev Conversation",
                TranscriptJson = transcriptJson,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        else
        {
            conversation.TranscriptJson = transcriptJson;
            conversation.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ClearAsync(string key, CancellationToken cancellationToken = default)
    {
        await dbContext.DevAgentConversations
            .Where(item => item.Key == key)
            .ExecuteDeleteAsync(cancellationToken);
    }
}

public sealed record AgentConversationMessage(string Role, string Text, DateTimeOffset CreatedAt)
{
    public ChatMessage ToChatMessage()
    {
        var role = string.Equals(Role, "assistant", StringComparison.OrdinalIgnoreCase)
            ? ChatRole.Assistant
            : ChatRole.User;

        return new ChatMessage(role, Text);
    }

    public static AgentConversationMessage User(string text)
    {
        return new("user", text, DateTimeOffset.UtcNow);
    }

    public static AgentConversationMessage Assistant(string text)
    {
        return new("assistant", text, DateTimeOffset.UtcNow);
    }
}
