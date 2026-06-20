using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Starter.Experiment
{
    public class MockChatClient : IChatClient
    {
        public void Dispose() { }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            // 直接拿最后一条消息的文本，防范 Role 不一致或过滤丢失的情况
            var lastUserMessage = chatMessages.LastOrDefault()?.Text ?? "empty prompt";

            // 1. 检查是不是有 FunctionResultContent (Tool 执行完成后的反馈)
            var returnContent = chatMessages
                .SelectMany(m => m.Contents)
                .OfType<FunctionResultContent>()
                .LastOrDefault();

            if (returnContent != null)
            {
                var resultText = returnContent.Result?.ToString() ?? "no result";
                var responseText = $"[Mock Response with Tool] I processed the tool result (CallId: {returnContent.CallId}) which returned: \"{resultText}\". Therefore, the task is complete!";
                
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText))
                {
                    ModelId = "local-mock-model"
                });
            }

            // 2. 检查大模型是不是决定调用 Tools
            if (lastUserMessage.Contains("weather", StringComparison.OrdinalIgnoreCase))
            {
                var callContent = new FunctionCallContent("call_weather_1", "GetCurrentWeather", new Dictionary<string, object?>
                {
                    { "location", "London" }
                });
                
                var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, [callContent]))
                {
                    ModelId = "local-mock-model"
                };
                return Task.FromResult(response);
            }
            
            if (lastUserMessage.Contains("stock", StringComparison.OrdinalIgnoreCase))
            {
                var callContent = new FunctionCallContent("call_stock_1", "GetStockPrice", new Dictionary<string, object?>
                {
                    { "ticker", "MSFT" }
                });
                
                var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, [callContent]))
                {
                    ModelId = "local-mock-model"
                };
                return Task.FromResult(response);
            }

            // 3. 检查是不是 Multi-Agent Workflow 流程中的 Editor Agent 阶段
            if (lastUserMessage.Contains("[Editor-Input]", StringComparison.OrdinalIgnoreCase))
            {
                var idx = lastUserMessage.IndexOf("[Editor-Input]", StringComparison.OrdinalIgnoreCase);
                var draft = lastUserMessage.Substring(idx + "[Editor-Input]".Length).Trim();
                var responseText = $"[Editor Agent Output]\nTitle: THE EXPANSION OF INTELLIGENCE\n\nPolished Draft:\n{draft.ToUpperInvariant()} (Reviewed & finalized by EditorAgent)";
                
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText))
                {
                    ModelId = "local-editor-model"
                });
            }

            // 4. 检查是不是 Multi-Agent Workflow 流程中的 Writer Agent 阶段
            if (lastUserMessage.Contains("space exploration", StringComparison.OrdinalIgnoreCase) ||
                lastUserMessage.Contains("topic", StringComparison.OrdinalIgnoreCase))
            {
                var responseText = $"[Writer Agent Output] Artificial intelligence will revolutionize space exploration, sending autonomous probes to navigate remote galaxies and discover uncharted planets.";
                
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText))
                {
                    ModelId = "local-writer-model"
                });
            }

            // 5. 常规回复
            var defaultResponse = $"[Mock Response] I received your prompt: \"{lastUserMessage}\". This response was generated locally.";
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, defaultResponse))
            {
                ModelId = "local-mock-model"
            });
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}
