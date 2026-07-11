using Elsa.Extensions;
using Elsa.Expressions.Models;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Memory;
using Elsa.Workflows.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Starter.Experiment
{
    // Scoped / Transient log collector service
    public class WorkflowLogger
    {
        private readonly List<string> _logs = new();
        private readonly object _lock = new();

        public void Log(string message)
        {
            lock (_lock)
            {
                _logs.Add($"[{System.DateTime.Now:HH:mm:ss}] {message}");
            }
        }

        public IReadOnlyList<string> GetLogs()
        {
            lock (_lock)
            {
                return _logs.ToList();
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _logs.Clear();
            }
        }
    }

    public class ResultTracker
    {
        public string Value { get; set; } = string.Empty;
    }

    public class WorkflowDiagnostics
    {
        private readonly List<LoopIterationResult> _loopIterations = new();
        private readonly List<ConditionDecision> _conditionDecisions = new();
        private readonly object _lock = new();

        public int NextLoopIteration()
        {
            lock (_lock)
            {
                return _loopIterations.Count + 1;
            }
        }

        public void AddLoopIteration(LoopIterationResult result)
        {
            lock (_lock)
            {
                _loopIterations.Add(result);
            }
        }

        public void AddConditionDecision(ConditionDecision decision)
        {
            lock (_lock)
            {
                _conditionDecisions.Add(decision);
            }
        }

        public IReadOnlyList<LoopIterationResult> GetLoopIterations()
        {
            lock (_lock)
            {
                return _loopIterations.ToList();
            }
        }

        public IReadOnlyList<ConditionDecision> GetConditionDecisions()
        {
            lock (_lock)
            {
                return _conditionDecisions.ToList();
            }
        }
    }

    // 1. Custom Activity: Initialize Order
    public class InitializeOrderActivity : CodeActivity
    {
        protected override void Execute(ActivityExecutionContext context)
        {
            var logger = context.GetRequiredService<WorkflowLogger>();
            
            var orderAmount = context.WorkflowInput.TryGetValue("OrderAmount", out var amountObj) ? Convert.ToDouble(amountObj) : 0.0;
            var customerVip = context.WorkflowInput.TryGetValue("CustomerVIP", out var vipObj) ? Convert.ToBoolean(vipObj) : false;
            var items = context.WorkflowInput.TryGetValue("Items", out var itemsObj) ? (ICollection<string>)itemsObj : new List<string>();

            logger.Log($"[InitializeOrderActivity] Order Amount: ${orderAmount:F2}, VIP: {customerVip}, Items Count: {items.Count}");

            context.SetVariable("OrderStatus", "Pending");

            if (items.Count == 0)
            {
                logger.Log("[InitializeOrderActivity] Order has no items. Rejecting order immediately.");
                context.SetVariable("OrderStatus", "Rejected");
            }
            else
            {
                logger.Log("[InitializeOrderActivity] Order is valid. Proceeding to item inventory check...");
            }
        }
    }

    // 2. Custom Activity: Process Item (Inventory Check)
    public class ProcessItemActivity : CodeActivity
    {
        public Input<ICollection<string>> OutOfStockKeywords { get; set; } = new(new List<string> { "out-of-stock", "sold-out" });

        protected override void Execute(ActivityExecutionContext context)
        {
            var logger = context.GetRequiredService<WorkflowLogger>();
            var diagnostics = context.GetRequiredService<WorkflowDiagnostics>();
            var item = context.GetVariable<string>("CurrentItem");
            var status = context.GetVariable<string>("OrderStatus");
            var iteration = diagnostics.NextLoopIteration();

            if (status == "Rejected")
            {
                logger.Log($"[ProcessItemActivity] Loop iteration {iteration}: skipping item '{item}' because OrderStatus is already Rejected.");
                diagnostics.AddLoopIteration(new LoopIterationResult(iteration, item ?? string.Empty, "Skipped", status));
                return;
            }

            logger.Log($"[ProcessItemActivity] Loop iteration {iteration}: checking inventory for item '{item}'...");

            var keywords = context.Get(OutOfStockKeywords) ?? new List<string> { "out-of-stock", "sold-out" };

            if (item != null && keywords.Any(kw => item.Contains(kw, StringComparison.OrdinalIgnoreCase)))
            {
                logger.Log($"[ProcessItemActivity] Loop iteration {iteration}: Item '{item}' is OUT OF STOCK. Rejecting order.");
                context.SetVariable("OrderStatus", "Rejected");
                diagnostics.AddLoopIteration(new LoopIterationResult(iteration, item, "OutOfStock", "Rejected"));
            }
            else
            {
                logger.Log($"[ProcessItemActivity] Loop iteration {iteration}: item '{item}' is in stock.");
                diagnostics.AddLoopIteration(new LoopIterationResult(iteration, item ?? string.Empty, "InStock", "Pending"));
            }
        }
    }

    // 3. Custom Activity: Evaluate Approval
    public class EvaluateApprovalActivity : CodeActivity
    {
        public Input<double> ApprovalThreshold { get; set; } = new(1000.0);

        protected override void Execute(ActivityExecutionContext context)
        {
            var logger = context.GetRequiredService<WorkflowLogger>();
            var diagnostics = context.GetRequiredService<WorkflowDiagnostics>();
            
            var amount = context.WorkflowInput.TryGetValue("OrderAmount", out var amountObj) ? Convert.ToDouble(amountObj) : 0.0;
            var vip = context.WorkflowInput.TryGetValue("CustomerVIP", out var vipObj) ? Convert.ToBoolean(vipObj) : false;
            var status = context.GetVariable<string>("OrderStatus");

            if (status == "Rejected")
            {
                logger.Log("[EvaluateApprovalActivity] Condition skipped because OrderStatus is Rejected.");
                diagnostics.AddConditionDecision(new ConditionDecision(
                    "Approval gate",
                    "OrderStatus != Rejected",
                    false,
                    "Skipped approval evaluation"));
                return;
            }

            var threshold = context.Get(ApprovalThreshold);

            logger.Log($"[EvaluateApprovalActivity] Checking approval thresholds: Amount = ${amount:F2}, VIP = {vip} (Threshold: ${threshold:F2})");

            if (amount > threshold)
            {
                if (vip)
                {
                    logger.Log($"[EvaluateApprovalActivity] Order amount > ${threshold:F2}, but customer is VIP. Auto-approving order.");
                    context.SetVariable("OrderStatus", "Approved");
                    diagnostics.AddConditionDecision(new ConditionDecision(
                        "High value VIP branch",
                        $"OrderAmount > {threshold:F2} && CustomerVIP",
                        true,
                        "Approved"));
                }
                else
                {
                    logger.Log($"[EvaluateApprovalActivity] Order amount > ${threshold:F2} and customer is NOT VIP. Escaped to Manual Review.");
                    context.SetVariable("OrderStatus", "ManualReview");
                    diagnostics.AddConditionDecision(new ConditionDecision(
                        "Manual review branch",
                        $"OrderAmount > {threshold:F2} && !CustomerVIP",
                        true,
                        "ManualReview"));
                }
            }
            else
            {
                logger.Log($"[EvaluateApprovalActivity] Order amount is under ${threshold:F2}. Auto-approving order.");
                context.SetVariable("OrderStatus", "Approved");
                diagnostics.AddConditionDecision(new ConditionDecision(
                    "Auto approval branch",
                    $"OrderAmount <= {threshold:F2}",
                    true,
                    "Approved"));
            }
        }
    }

    // 4. Custom Activity: Process Payment
    public class ProcessPaymentActivity : CodeActivity
    {
        protected override void Execute(ActivityExecutionContext context)
        {
            var logger = context.GetRequiredService<WorkflowLogger>();
            var diagnostics = context.GetRequiredService<WorkflowDiagnostics>();
            var status = context.GetVariable<string>("OrderStatus");

            if (status == "Approved")
            {
                var amount = context.WorkflowInput.TryGetValue("OrderAmount", out var amountObj) ? Convert.ToDouble(amountObj) : 0.0;
                logger.Log($"[ProcessPaymentActivity] Authorizing gateway charge of ${amount:F2}...");
                logger.Log($"[ProcessPaymentActivity] Gateway approved transaction. ID: txn_{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}");
                diagnostics.AddConditionDecision(new ConditionDecision(
                    "Payment condition",
                    "OrderStatus == Approved",
                    true,
                    "Payment processed"));
            }
            else
            {
                logger.Log($"[ProcessPaymentActivity] Order status is '{status}'. Skipping payment processing.");
                diagnostics.AddConditionDecision(new ConditionDecision(
                    "Payment condition",
                    "OrderStatus == Approved",
                    false,
                    "Payment skipped"));
            }
        }
    }

    // 5. Custom Activity: Finalize Order
    public class FinalizeOrderActivity : CodeActivity
    {
        protected override void Execute(ActivityExecutionContext context)
        {
            var logger = context.GetRequiredService<WorkflowLogger>();
            var tracker = context.GetRequiredService<ResultTracker>();
            var status = context.GetVariable<string>("OrderStatus");
            
            var amount = context.WorkflowInput.TryGetValue("OrderAmount", out var amountObj) ? Convert.ToDouble(amountObj) : 0.0;

            logger.Log($"[FinalizeOrderActivity] Final Order Status: {status}");
            tracker.Value = $"Processed Order: ${amount:F2} -> {status}";
        }
    }

    public static class ElsaExperiment
    {
        // Keep the old simple method to avoid compile errors elsewhere
        public static async Task<string> RunSimpleWorkflowAsync(string message, CancellationToken cancellationToken = default)
        {
            var services = new ServiceCollection();
            services.AddElsa();

            var tracker = new ResultTracker();
            var logger = new WorkflowLogger();
            var diagnostics = new WorkflowDiagnostics();
            services.AddSingleton(tracker);
            services.AddSingleton(logger);
            services.AddSingleton(diagnostics);

            var serviceProvider = services.BuildServiceProvider();
            var workflowRunner = serviceProvider.GetRequiredService<IWorkflowRunner>();

            var workflow = new Sequence
            {
                Activities =
                {
                    new WriteLine($"Input message to workflow: {message}"),
                    new TrackResultActivity { MessageToTrack = message }
                }
            };

            await workflowRunner.RunAsync(workflow, cancellationToken: cancellationToken);

            var resultTracker = serviceProvider.GetRequiredService<ResultTracker>();
            return resultTracker.Value;
        }

        // The new Order Processing workflow demonstrating conditionals, loops, variables and custom activities
        public static async Task<OrderWorkflowResult> RunOrderWorkflowAsync(
            double orderAmount, 
            bool customerVip, 
            ICollection<string> items, 
            CancellationToken cancellationToken = default)
        {
            return await RunOrderWorkflowAsync(orderAmount, customerVip, items, new WorkflowSchema(), cancellationToken);
        }

        public static async Task<OrderWorkflowResult> RunOrderWorkflowAsync(
            double orderAmount, 
            bool customerVip, 
            ICollection<string> items,
            WorkflowSchema schema,
            CancellationToken cancellationToken = default)
        {
            var services = new ServiceCollection();
            services.AddElsa();

            var tracker = new ResultTracker();
            var logger = new WorkflowLogger();
            var diagnostics = new WorkflowDiagnostics();
            services.AddSingleton(tracker);
            services.AddSingleton(logger);
            services.AddSingleton(diagnostics);

            var serviceProvider = services.BuildServiceProvider();
            var workflowRunner = serviceProvider.GetRequiredService<IWorkflowRunner>();

            // Define variables
            var orderStatus = new Variable<string>("OrderStatus", "Pending");
            var currentItem = new Variable<string>("CurrentItem", "");

            var activities = new List<IActivity>();

            // 1. Initialize Order
            if (schema.EnableInitializeOrder)
            {
                activities.Add(new InitializeOrderActivity());
            }

            // Outer condition: evaluate items loop and approval if status is not Rejected
            var thenActivities = new List<IActivity>();

            // 2. Inventory Loop Check
            if (schema.EnableInventoryCheck)
            {
                thenActivities.Add(new ForEach<string>
                {
                    Items = new Input<ICollection<string>>(context => (ICollection<string>)context.GetActivityExecutionContext().WorkflowInput["Items"]),
                    CurrentValue = new Output<string>(currentItem),
                    Body = new ProcessItemActivity
                    {
                        OutOfStockKeywords = new Input<ICollection<string>>(schema.OutOfStockKeywords)
                    }
                });
            }

            // Inner condition for evaluation & payment
            var innerActivities = new List<IActivity>();
            
            if (schema.EnableEvaluateApproval)
            {
                innerActivities.Add(new EvaluateApprovalActivity
                {
                    ApprovalThreshold = new Input<double>(schema.ApprovalThreshold)
                });
            }

            if (schema.EnableProcessPayment)
            {
                innerActivities.Add(new ProcessPaymentActivity());
            }

            if (innerActivities.Any())
            {
                thenActivities.Add(new If(context => RecordCondition(
                    context,
                    "Continue after inventory loop",
                    "OrderStatus != Rejected",
                    context.GetVariable<string>("OrderStatus") != "Rejected",
                    "Run approval and payment branch",
                    "Skip approval and payment branch"))
                {
                    Then = new Sequence { Activities = innerActivities }
                });
            }

            if (thenActivities.Any())
            {
                activities.Add(new If(context => RecordCondition(
                    context,
                    "Continue after initialization",
                    "OrderStatus != Rejected",
                    context.GetVariable<string>("OrderStatus") != "Rejected",
                    "Run inventory loop and approval gates",
                    "Skip inventory loop and approval gates"))
                {
                    Then = new Sequence { Activities = thenActivities }
                });
            }

            // 3. Custom WriteLine Log (if enabled)
            if (schema.EnableCustomLog)
            {
                activities.Add(new WriteLine(schema.CustomLogMessage));
            }

            // 4. Finalize Order
            if (schema.EnableFinalizeOrder)
            {
                activities.Add(new FinalizeOrderActivity());
            }

            var workflow = new Sequence
            {
                Variables = { orderStatus, currentItem },
                Activities = activities
            };

            var options = new Elsa.Workflows.Options.RunWorkflowOptions
            {
                Input = new Dictionary<string, object>
                {
                    { "OrderAmount", orderAmount },
                    { "CustomerVIP", customerVip },
                    { "Items", items }
                }
            };

            var runResult = await workflowRunner.RunAsync(workflow, options, cancellationToken: cancellationToken);

            logger.Log($"[Workflow RunResult] Status: {runResult.WorkflowState.Status}, SubStatus: {runResult.WorkflowState.SubStatus}, Incidents: {runResult.WorkflowState.Incidents.Count}");

            // Suspended diagnostics
            if (runResult.WorkflowState.SubStatus == WorkflowSubStatus.Suspended)
            {
                logger.Log($"[WORKFLOW SUSPENDED] Bookmarks count: {runResult.WorkflowState.Bookmarks.Count}");
                foreach (var bookmark in runResult.WorkflowState.Bookmarks)
                {
                    logger.Log($"[Bookmark] Activity ID: {bookmark.ActivityId}, Name: {bookmark.Name}, Hash: {bookmark.Hash}");
                }
                foreach (var activityState in runResult.WorkflowState.ActivityExecutionContexts)
                {
                    logger.Log($"[Active Activity] Node ID: {activityState.ScheduledActivityNodeId}, Status: {activityState.Status}");
                }
            }

            // Detailed fault diagnosis logging
            if (runResult.WorkflowState.SubStatus == WorkflowSubStatus.Faulted || runResult.WorkflowState.Incidents.Any())
            {
                logger.Log($"[WORKFLOW FAULTED] Status: {runResult.WorkflowState.Status}, SubStatus: {runResult.WorkflowState.SubStatus}");
                foreach (var incident in runResult.WorkflowState.Incidents)
                {
                    logger.Log($"[Incident] Activity ID: {incident.ActivityId} ({incident.ActivityType}), Message: {incident.Message}");
                    if (incident.Exception != null)
                    {
                        logger.Log($"[Exception] {incident.Exception.Type}: {incident.Exception.Message}");
                        logger.Log($"[Stack Trace] {incident.Exception.StackTrace}");
                    }
                }
            }

            return new OrderWorkflowResult(
                tracker.Value,
                logger.GetLogs(),
                diagnostics.GetLoopIterations(),
                diagnostics.GetConditionDecisions());
        }

        private static bool RecordCondition(
            ExpressionExecutionContext context,
            string name,
            string expression,
            bool result,
            string trueOutcome,
            string falseOutcome)
        {
            var logger = context.GetRequiredService<WorkflowLogger>();
            var diagnostics = context.GetRequiredService<WorkflowDiagnostics>();
            var outcome = result ? trueOutcome : falseOutcome;

            logger.Log($"[Condition] {name}: {expression} => {result}. Outcome: {outcome}.");
            diagnostics.AddConditionDecision(new ConditionDecision(name, expression, result, outcome));

            return result;
        }
    }

    public class WorkflowSchema
    {
        public double ApprovalThreshold { get; set; } = 1000.0;
        public ICollection<string> OutOfStockKeywords { get; set; } = new List<string> { "out-of-stock", "sold-out" };
        
        public bool EnableInitializeOrder { get; set; } = true;
        public bool EnableInventoryCheck { get; set; } = true;
        public bool EnableEvaluateApproval { get; set; } = true;
        public bool EnableProcessPayment { get; set; } = true;
        public bool EnableFinalizeOrder { get; set; } = true;
        
        public bool EnableCustomLog { get; set; } = false;
        public string CustomLogMessage { get; set; } = "Custom write line log";
    }

    public sealed record LoopIterationResult(
        int Iteration,
        string Item,
        string Outcome,
        string OrderStatusAfterIteration);

    public sealed record ConditionDecision(
        string Name,
        string Expression,
        bool Result,
        string Outcome);

    public sealed record OrderWorkflowResult(
        string FinalValue,
        IReadOnlyList<string> Logs,
        IReadOnlyList<LoopIterationResult> LoopIterations,
        IReadOnlyList<ConditionDecision> ConditionDecisions);

    public class TrackResultActivity : CodeActivity
    {
        public string MessageToTrack { get; set; } = string.Empty;

        protected override void Execute(ActivityExecutionContext context)
        {
            var tracker = context.GetRequiredService<ResultTracker>();
            tracker.Value = $"Processed: {MessageToTrack}";
        }
    }
}
