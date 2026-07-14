using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Starter.Experiment;

namespace Starter.Tests;

public class ExperimentTests
{
    [Fact]
    public async Task Simple_succeeds()
    {
        // Arrange

        // Act
        await Task.Delay(1, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(true);
    }

    [Fact]
    public async Task Elsa_SimpleWorkflow_RunsSuccessfully()
    {
        // Arrange
        var message = "Test Elsa Workflow";
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var result = await ElsaExperiment.RunSimpleWorkflowAsync(message, cancellationToken);

        // Assert
        Assert.Equal("Processed: Test Elsa Workflow", result);
    }

    [Fact]
    public async Task Elsa_OrderWorkflow_AutoApprove_Succeeds()
    {
        // Arrange
        var items = new List<string> { "laptop", "mouse" };
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var result = await ElsaExperiment.RunOrderWorkflowAsync(250.00, false, items, cancellationToken);

        // Assert
        foreach (var log in result.Logs)
        {
            Console.WriteLine(log);
        }
        Assert.Contains("Approved", result.FinalValue);
        Assert.Equal(2, result.LoopIterations.Count);
        Assert.All(result.LoopIterations, iteration => Assert.Equal("InStock", iteration.Outcome));
        Assert.Contains(result.ConditionDecisions, decision => decision.Name == "Auto approval branch" && decision.Outcome == "Approved");
        Assert.Contains(result.ConditionDecisions, decision => decision.Name == "Payment condition" && decision.Result);
        Assert.Contains(result.Logs, l => l.Contains("[ProcessPaymentActivity] Authorizing gateway charge"));
        Assert.Contains(result.Logs, l => l.Contains("[FinalizeOrderActivity] Final Order Status: Approved"));
    }

    [Fact]
    public async Task Elsa_OrderWorkflow_OutOfStock_Rejects()
    {
        // Arrange
        var items = new List<string> { "laptop", "iphone [out-of-stock]" };
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var result = await ElsaExperiment.RunOrderWorkflowAsync(450.00, false, items, cancellationToken);

        // Assert
        foreach (var log in result.Logs)
        {
            Console.WriteLine(log);
        }
        Assert.Contains("Rejected", result.FinalValue);
        Assert.Contains(result.LoopIterations, iteration => iteration.Item == "iphone [out-of-stock]" && iteration.Outcome == "OutOfStock");
        Assert.Contains(result.ConditionDecisions, decision => decision.Name == "Continue after inventory loop" && !decision.Result);
        Assert.Contains(result.Logs, l => l.Contains("Item 'iphone [out-of-stock]' is OUT OF STOCK"));
        Assert.Contains(result.Logs, l => l.Contains("[FinalizeOrderActivity] Final Order Status: Rejected"));
    }

    [Fact]
    public async Task Elsa_OrderWorkflow_HighValueNonVip_EscalatesToManualReview()
    {
        // Arrange
        var items = new List<string> { "macbook" };
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var result = await ElsaExperiment.RunOrderWorkflowAsync(1500.00, false, items, cancellationToken);

        // Assert
        foreach (var log in result.Logs)
        {
            Console.WriteLine(log);
        }
        Assert.Contains("ManualReview", result.FinalValue);
        Assert.Single(result.LoopIterations);
        Assert.Contains(result.ConditionDecisions, decision => decision.Name == "Manual review branch" && decision.Outcome == "ManualReview");
        Assert.Contains(result.ConditionDecisions, decision => decision.Name == "Payment condition" && !decision.Result);
        Assert.Contains(result.Logs, l => l.Contains("customer is NOT VIP. Escaped to Manual Review."));
        Assert.Contains(result.Logs, l => l.Contains("Skipping payment processing"));
        Assert.Contains(result.Logs, l => l.Contains("[FinalizeOrderActivity] Final Order Status: ManualReview"));
    }

    [Fact]
    public async Task Elsa_OrderWorkflow_OutOfStock_SkipsRemainingLoopItems()
    {
        // Arrange
        var items = new List<string> { "laptop", "iphone [out-of-stock]", "mouse" };
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var result = await ElsaExperiment.RunOrderWorkflowAsync(450.00, false, items, cancellationToken);

        // Assert
        Assert.Contains("Rejected", result.FinalValue);
        Assert.Equal(3, result.LoopIterations.Count);
        Assert.Collection(
            result.LoopIterations,
            iteration => Assert.Equal("InStock", iteration.Outcome),
            iteration => Assert.Equal("OutOfStock", iteration.Outcome),
            iteration => Assert.Equal("Skipped", iteration.Outcome));
        Assert.Contains(result.ConditionDecisions, decision => decision.Name == "Continue after inventory loop" && !decision.Result);
    }

    [Fact]
    public async Task AgentAi_ChatClientAgent_RunsSuccessfully()
    {
        // Arrange
        var mockClient = new MockChatClient();
        var prompt = "What is the capital of Germany?";
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var result = await AgentAiExperiment.RunAgentAsync(mockClient, prompt, "Simple", null, cancellationToken);

        // Assert
        Assert.Contains("[Mock Response]", result);
        Assert.Contains(prompt, result);
    }

    [Fact]
    public async Task AgentAi_ChatClientAgent_WithTools_RunsSuccessfully()
    {
        // Arrange
        var mockClient = new MockChatClient();
        var prompt = "What is the weather in London?";
        var cancellationToken = TestContext.Current.CancellationToken;
        var logs = new List<string>();

        // Act
        var result = await AgentAiExperiment.RunAgentAsync(mockClient, prompt, "Tools", logs.Add, cancellationToken);

        // Assert
        Assert.Contains("[Mock Response with Tool]", result);
        Assert.Contains("call_weather_1", result);
        Assert.Contains(logs, log => log.Contains("[LLM Decision]") && log.Contains("GetCurrentWeather"));
        Assert.Contains(logs, log => log.Contains("[Tool Execution]") && log.Contains("rainy"));
    }

    [Fact]
    public async Task AgentAi_ChatClientAgent_Guardrails_BlocksSensitivePrompts()
    {
        // Arrange
        var mockClient = new MockChatClient();
        var prompt = "How to hack into a system?";
        var cancellationToken = TestContext.Current.CancellationToken;
        var logs = new List<string>();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AgentAiExperiment.RunAgentAsync(mockClient, prompt, "Guardrails", logs.Add, cancellationToken)
        );

        Assert.Contains("Guardrails violation", exception.Message);
        Assert.Contains(logs, log => log.Contains("[Middleware Pipeline]") && log.Contains("violation detected"));
    }

    [Fact]
    public async Task AgentAi_MultiAgentWorkflow_RunsSuccessfully()
    {
        // Arrange
        var mockClient = new MockChatClient();
        var topic = "AI in space exploration";
        var cancellationToken = TestContext.Current.CancellationToken;
        var logs = new List<string>();

        // Act
        var result = await AgentAiExperiment.RunMultiAgentWorkflowAsync(mockClient, topic, logs.Add, cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("[Writer Agent Output]", result.DraftText);
        Assert.Contains("[Editor Agent Output]", result.FinalText);
        Assert.Contains("THE EXPANSION OF INTELLIGENCE", result.FinalText);
        Assert.Contains(logs, log => log.Contains("[Workflow]") && log.Contains("WriterAgent"));
        Assert.Contains(logs, log => log.Contains("[Workflow]") && log.Contains("EditorAgent"));
    }
}
