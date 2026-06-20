using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Starter.Experiment
{
    // 1. 定义 C# 本地函数作为大模型 Tools
    public static class AgentTools
    {
        [Description("Get the current weather forecast for a specific location.")]
        public static string GetCurrentWeather([Description("The city and state, e.g. London, Tokyo")] string location)
        {
            return $"The weather in {location} is currently rainy and 14°C. High probability of precipitation.";
        }

        [Description("Get the current stock price for a given stock ticker.")]
        public static string GetStockPrice([Description("The stock ticker symbol, e.g. MSFT, AAPL")] string ticker)
        {
            return $"The stock price of {ticker.ToUpperInvariant()} is currently $435.20 (+1.4%).";
        }
    }

    // 2. Logging ChatClient 中间件，自动捕获请求报文、大模型 Decision 和 Tool 执行细节
    public class LoggingChatClient(IChatClient innerClient, Action<string> logCallback) : DelegatingChatClient(innerClient)
    {
        public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            // 捕获并记录发送请求
            var lastMsg = chatMessages.LastOrDefault();
            logCallback($"[Client Message] Sending content to LLM: \"{lastMsg?.Text ?? "empty"}\"");

            // 检查是不是 Tool 执行后的结果回传 (采用 FunctionResultContent)
            var returnMessages = lastMsg?.Contents.OfType<FunctionResultContent>().ToList();
            if (returnMessages != null && returnMessages.Any())
            {
                foreach (var ret in returnMessages)
                {
                    logCallback($"[Tool Execution] Local function tool (CallId: {ret.CallId}) executed successfully. Returned: \"{ret.Result}\"");
                }
            }

            var response = await base.GetResponseAsync(chatMessages, options, cancellationToken);

            // 检查大模型是不是发出了 Tool Call 请求
            var toolCalls = response.Messages.FirstOrDefault()?.Contents.OfType<FunctionCallContent>().ToList();
            if (toolCalls != null && toolCalls.Any())
            {
                foreach (var call in toolCalls)
                {
                    logCallback($"[LLM Decision] AI requested tool call to function '{call.Name}' with args: {System.Text.Json.JsonSerializer.Serialize(call.Arguments)}");
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
        // 升级的 RunAgentAsync 方法，支持不同模式（Simple, Tools, Guardrails）并能输出日志
        public static async Task<string> RunAgentAsync(
            IChatClient chatClient, 
            string prompt, 
            string exampleMode = "Simple", 
            Action<string>? logCallback = null,
            CancellationToken cancellationToken = default)
        {
            var logger = logCallback ?? (_ => {});

            // 1. 构建 LoggingChatClient 包装，记录请求和应答日志
            var loggingClient = new LoggingChatClient(chatClient, logger);

            // 2. 根据不同的示例模式进行构建
            if (exampleMode == "Tools")
            {
                logger("[Setup] Enabling automatic function invocation middleware...");
                logger("[Setup] Binding local C# tools: GetCurrentWeather, GetStockPrice.");

                // 使用 ChatClientBuilder 挂载 Function Invocation，实现自动执行 Tools 并反馈给大模型
                var toolPipeline = new ChatClientBuilder(loggingClient)
                    .UseFunctionInvocation()
                    .Build();

                IList<AITool> tools = [
                    AIFunctionFactory.Create(AgentTools.GetCurrentWeather),
                    AIFunctionFactory.Create(AgentTools.GetStockPrice)
                ];

                var agent = new ChatClientAgent(
                    toolPipeline,
                    instructions: "You are a helpful assistant with access to local weather and stock tools. Use tools when needed.",
                    name: "ToolCapableAgent",
                    tools: tools
                );

                var response = await agent.RunAsync(prompt, session: null, options: null, cancellationToken: cancellationToken);
                return response.Text;
            }
            else if (exampleMode == "Guardrails")
            {
                logger("[Setup] Setting up AIAgentBuilder middleware pipeline...");
                logger("[Setup] Registering Guardrails safety policy check.");

                var baseAgent = new ChatClientAgent(
                    loggingClient,
                    instructions: "You are a friendly agent. You must answer concisely.",
                    name: "BaseAgent"
                );

                var builder = new AIAgentBuilder(baseAgent);

                // 使用 Use(runFunc, runStreamingFunc) 中间件对消息输入进行审查过滤
                builder.Use(
                    async (messages, session, options, nextAgent, cancellationToken) =>
                    {
                        logger("[Middleware Pipeline] Guardrail check started.");

                        var userMsg = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text;
                        if (userMsg != null && (
                            userMsg.Contains("hack", StringComparison.OrdinalIgnoreCase) ||
                            userMsg.Contains("explode", StringComparison.OrdinalIgnoreCase) ||
                            userMsg.Contains("password", StringComparison.OrdinalIgnoreCase)))
                        {
                            logger("[Middleware Pipeline] Guardrails safety violation detected! Request blocked.");
                            throw new InvalidOperationException("Guardrails violation: sensitive query blocked.");
                        }

                        logger("[Middleware Pipeline] Safety policy passed. Executing next step...");
                        var res = await nextAgent.RunAsync(messages, session, options, cancellationToken);
                        logger("[Middleware Pipeline] Pipeline execution completed.");
                        return res;
                    },
                    runStreamingFunc: null
                );

                var secureAgent = builder.Build();
                var response = await secureAgent.RunAsync(prompt, session: null, options: null, cancellationToken: cancellationToken);
                return response.Text;
            }
            else // Simple Mode
            {
                logger("[Setup] Running in basic conversational mode.");
                var agent = new ChatClientAgent(
                    loggingClient,
                    instructions: "You are a friendly assistant created for Starter.Experiment project. You must answer concisely.",
                    name: "SimpleAgent"
                );

                var response = await agent.RunAsync(prompt, session: null, options: null, cancellationToken: cancellationToken);
                return response.Text;
            }
        }

        // 新增的 RunMultiAgentWorkflowAsync 用于演示串联的多智能体工作流（Writer-Editor Chaining）
        public static async Task<MultiAgentWorkflowResult> RunMultiAgentWorkflowAsync(
            IChatClient chatClient,
            string topic,
            Action<string>? logCallback = null,
            CancellationToken cancellationToken = default)
        {
            var logger = logCallback ?? (_ => {});
            var loggingClient = new LoggingChatClient(chatClient, logger);

            // 1. 设置 Writer Agent
            logger("[Workflow] Constructing WriterAgent...");
            var writer = new ChatClientAgent(
                loggingClient,
                instructions: "You are a creative writer. Draft a short, engaging article about the given topic in 2-3 sentences.",
                name: "WriterAgent"
            );

            logger($"[Workflow] WriterAgent is drafting article for topic: \"{topic}\"");
            var writerResponse = await writer.RunAsync(topic, session: null, options: null, cancellationToken: cancellationToken);
            var draftText = writerResponse.Text;
            logger($"[Workflow] WriterAgent completed draft. Output:\n\"{draftText}\"");

            // 2. 设置 Editor Agent 并执行润色 (输入携带 [Editor-Input] 前缀好让 MockClient 识别)
            logger("[Workflow] Constructing EditorAgent...");
            var editor = new ChatClientAgent(
                loggingClient,
                instructions: "You are a professional editor. Improve the draft by fixing grammar, polishing tone, and adding a formal title. Return the final draft.",
                name: "EditorAgent"
            );

            var editorInput = $"[Editor-Input] {draftText}";
            logger("[Workflow] Handing off draft to EditorAgent...");
            var editorResponse = await editor.RunAsync(editorInput, session: null, options: null, cancellationToken: cancellationToken);
            var finalText = editorResponse.Text;
            logger($"[Workflow] EditorAgent completed editing. Final result ready.");

            return new MultiAgentWorkflowResult(draftText, finalText);
        }
    }

    public sealed record MultiAgentWorkflowResult(string DraftText, string FinalText);
}
