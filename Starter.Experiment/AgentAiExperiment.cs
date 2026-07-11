using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Text.Json;

namespace Starter.Experiment;

public static class AgentTools
{
    [Description("Get the current weather forecast for a specific location.")]
    public static string GetCurrentWeather([Description("The city and state, e.g. London, Tokyo")] string location)
    {
        return $"The weather in {location} is currently rainy and 14 C. High probability of precipitation.";
    }

    [Description("Get the current stock price for a given stock ticker.")]
    public static string GetStockPrice([Description("The stock ticker symbol, e.g. MSFT, AAPL")] string ticker)
    {
        return $"The stock price of {ticker.ToUpperInvariant()} is currently $435.20 (+1.4%).";
    }
}

public class LoggingChatClient(IChatClient innerClient, Action<string> logCallback) : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var lastMessage = chatMessages.LastOrDefault();
        logCallback($"[Client Message] Sending content to LLM: \"{lastMessage?.Text ?? "empty"}\"");

        var functionResults = lastMessage?.Contents.OfType<FunctionResultContent>().ToList();
        if (functionResults is { Count: > 0 })
        {
            foreach (var result in functionResults)
            {
                logCallback($"[Tool Execution] Tool result (CallId: {result.CallId}) returned: \"{result.Result}\"");
            }
        }

        var response = await base.GetResponseAsync(chatMessages, options, cancellationToken);

        var toolCalls = response.Messages.FirstOrDefault()?.Contents.OfType<FunctionCallContent>().ToList();
        if (toolCalls is { Count: > 0 })
        {
            foreach (var call in toolCalls)
            {
                logCallback($"[LLM Decision] AI requested tool call '{call.Name}' with args: {JsonSerializer.Serialize(call.Arguments)}");
            }
        }
        else
        {
            logCallback($"[LLM Output] Received text response: \"{response.Text}\"");
        }

        return response;
    }
}

public static class AgentAiExperiment
{
    public static Task<string> RunAgentAsync(
        IChatClient chatClient,
        string prompt,
        string exampleMode = "Simple",
        Action<string>? logCallback = null,
        CancellationToken cancellationToken = default)
    {
        return RunAgentAsync(chatClient, prompt, [], exampleMode, null, null, logCallback, cancellationToken);
    }

    public static async Task<string> RunAgentAsync(
        IChatClient chatClient,
        string prompt,
        IReadOnlyList<ChatMessage> conversationHistory,
        string exampleMode,
        IList<AITool>? tools,
        string? toolInstructions,
        Action<string>? logCallback = null,
        CancellationToken cancellationToken = default)
    {
        var logger = logCallback ?? (_ => { });
        var loggingClient = new LoggingChatClient(chatClient, logger);

        if (exampleMode == "Tools")
        {
            logger("[Setup] Enabling automatic function invocation middleware.");

            var toolPipeline = new ChatClientBuilder(loggingClient)
                .UseFunctionInvocation()
                .Build();

            tools ??=
            [
                AIFunctionFactory.Create(AgentTools.GetCurrentWeather),
                AIFunctionFactory.Create(AgentTools.GetStockPrice),
            ];

            var toolNames = string.Join(", ", tools.OfType<AIFunctionDeclaration>().Select(tool => tool.Name));
            logger($"[Setup] Binding tools: {toolNames}.");

            var agent = new ChatClientAgent(
                toolPipeline,
                instructions: toolInstructions
                    ?? "You are a helpful assistant with access to local weather and stock tools. Use tools when needed.",
                name: "ToolCapableAgent",
                tools: tools);

            var messages = BuildConversation(conversationHistory, prompt);
            var response = await agent.RunAsync(
                messages,
                session: null,
                options: new ChatClientAgentRunOptions
                {
                    ChatOptions = new ChatOptions
                    {
                        AllowMultipleToolCalls = true,
                    },
                },
                cancellationToken: cancellationToken);

            return response.Text;
        }

        if (exampleMode == "Guardrails")
        {
            logger("[Setup] Setting up AIAgentBuilder middleware pipeline.");
            logger("[Setup] Registering guardrails safety policy check.");

            var baseAgent = new ChatClientAgent(
                loggingClient,
                instructions: "You are a friendly agent. You must answer concisely.",
                name: "BaseAgent");

            var builder = new AIAgentBuilder(baseAgent);

            builder.Use(
                async (messages, session, options, nextAgent, cancellationToken) =>
                {
                    logger("[Middleware Pipeline] Guardrail check started.");

                    var userMessage = messages.LastOrDefault(message => message.Role == ChatRole.User)?.Text;
                    if (userMessage is not null
                        && (userMessage.Contains("hack", StringComparison.OrdinalIgnoreCase)
                            || userMessage.Contains("explode", StringComparison.OrdinalIgnoreCase)
                            || userMessage.Contains("password", StringComparison.OrdinalIgnoreCase)))
                    {
                        logger("[Middleware Pipeline] Guardrails safety violation detected. Request blocked.");
                        throw new InvalidOperationException("Guardrails violation: sensitive query blocked.");
                    }

                    logger("[Middleware Pipeline] Safety policy passed. Executing next step.");
                    var result = await nextAgent.RunAsync(messages, session, options, cancellationToken);
                    logger("[Middleware Pipeline] Pipeline execution completed.");
                    return result;
                },
                runStreamingFunc: null);

            var secureAgent = builder.Build();
            var response = await secureAgent.RunAsync(prompt, session: null, options: null, cancellationToken: cancellationToken);
            return response.Text;
        }

        logger("[Setup] Running in basic conversational mode.");
        var simpleAgent = new ChatClientAgent(
            loggingClient,
            instructions: "You are a friendly assistant created for Starter.Experiment project. You must answer concisely.",
            name: "SimpleAgent");

        var simpleResponse = await simpleAgent.RunAsync(prompt, session: null, options: null, cancellationToken: cancellationToken);
        return simpleResponse.Text;
    }

    public static async Task<MultiAgentWorkflowResult> RunMultiAgentWorkflowAsync(
        IChatClient chatClient,
        string topic,
        Action<string>? logCallback = null,
        CancellationToken cancellationToken = default)
    {
        var logger = logCallback ?? (_ => { });
        var loggingClient = new LoggingChatClient(chatClient, logger);

        logger("[Workflow] Constructing WriterAgent.");
        var writer = new ChatClientAgent(
            loggingClient,
            instructions: "You are a creative writer. Draft a short, engaging article about the given topic in 2-3 sentences.",
            name: "WriterAgent");

        logger($"[Workflow] WriterAgent is drafting article for topic: \"{topic}\"");
        var writerResponse = await writer.RunAsync(topic, session: null, options: null, cancellationToken: cancellationToken);
        var draftText = writerResponse.Text;
        logger($"[Workflow] WriterAgent completed draft. Output:\n\"{draftText}\"");

        logger("[Workflow] Constructing EditorAgent.");
        var editor = new ChatClientAgent(
            loggingClient,
            instructions: "You are a professional editor. Improve the draft by fixing grammar, polishing tone, and adding a formal title. Return the final draft.",
            name: "EditorAgent");

        var editorInput = $"[Editor-Input] {draftText}";
        logger("[Workflow] Handing off draft to EditorAgent.");
        var editorResponse = await editor.RunAsync(editorInput, session: null, options: null, cancellationToken: cancellationToken);
        var finalText = editorResponse.Text;
        logger("[Workflow] EditorAgent completed editing. Final result ready.");

        return new MultiAgentWorkflowResult(draftText, finalText);
    }

    private static List<ChatMessage> BuildConversation(
        IReadOnlyList<ChatMessage> conversationHistory,
        string prompt)
    {
        var messages = conversationHistory
            .Where(message => message.Role == ChatRole.User || message.Role == ChatRole.Assistant)
            .ToList();

        messages.Add(new ChatMessage(ChatRole.User, prompt));
        return messages;
    }
}

public sealed record MultiAgentWorkflowResult(string DraftText, string FinalText);
