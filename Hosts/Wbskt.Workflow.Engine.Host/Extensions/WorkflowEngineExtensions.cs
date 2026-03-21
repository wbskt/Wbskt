using Wbskt.Primitives;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Engine.Host.Interfaces;
using Wbskt.Workflow.Engine.Host.Services;
using Wbskt.Workflow.Engine.Host.Services.ExpressionEvaluators;
using Wbskt.Workflow.Engine.Host.Services.NodeExecutors;
using Wbskt.Workflow.Providers;

namespace Wbskt.Workflow.Engine.Host.Extensions;

public static class WorkflowEngineExtensions
{
    public static void AddWorkflowEngine(this IServiceCollection services)
    {
        services.AddSingleton<IWorkflowRuntimeRegistry, WorkflowRuntimeRegistry>();
        services.AddSingleton<IWorkflowExpressionEvaluator, WorkflowExpressionEvaluator>();
        services.AddSingleton<IWorkflowEngine, WorkflowEngine>();
        services.AddScoped<IWorkflowProvider, WorkflowProvider>();

        // Register Node Executors using Keyed Scopes
        // Triggers
        services.AddKeyedScoped<IWorkflowNodeExecutor, DeviceTriggerExecutor>(nameof(DeviceTriggerNode));
        
        // Controls
        services.AddKeyedScoped<IWorkflowNodeExecutor, LogicGateExecutor>(nameof(LogicGateNode));
        services.AddKeyedScoped<IWorkflowNodeExecutor, VariableExecutor>(nameof(VariableNode));
        services.AddKeyedScoped<IWorkflowNodeExecutor, DelayExecutor>(nameof(DelayNode));
        services.AddKeyedScoped<IWorkflowNodeExecutor, LoopExecutor>(nameof(LoopNode));
        
        // Actions
        services.AddKeyedScoped<IWorkflowNodeExecutor, SendCommandActionExecutor>(nameof(SendCommandActionNode));
        services.AddKeyedScoped<IWorkflowNodeExecutor, ToastNotificationExecutor>(nameof(ToastNotificationNode));
        services.AddKeyedScoped<IWorkflowNodeExecutor, EmailNotificationExecutor>(nameof(EmailNotificationNode));
        services.AddKeyedScoped<IWorkflowNodeExecutor, TelegramNotificationExecutor>(nameof(TelegramNotificationNode));
        services.AddKeyedScoped<IWorkflowNodeExecutor, WebhookNotificationExecutor>(nameof(WebhookNotificationNode));

        // Registry Initialization Task
        services.AddTransient<IStartupTask, WorkflowRegistryInitializationTask>();
    }
}
