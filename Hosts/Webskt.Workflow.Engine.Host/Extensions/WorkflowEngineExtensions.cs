using Webskt.Common.Abstraction.Interfaces;
using Webskt.Workflow.Engine.Host.Interfaces;
using Webskt.Workflow.Engine.Host.Services;
using Webskt.Workflow.Engine.Host.Services.ExpressionEvaluators;
using Webskt.Workflow.Providers;

namespace Webskt.Workflow.Engine.Host.Extensions;

public static class WorkflowEngineExtensions
{
    public static void AddWorkflowEngine(this IServiceCollection services)
    {
        services.AddSingleton<IWorkflowRuntimeRegistry, WorkflowRuntimeRegistry>();
        services.AddSingleton<IWorkflowExpressionEvaluator, WorkflowExpressionEvaluator>();
        services.AddScoped<IWorkflowEngine, WorkflowEngine>();
        services.AddScoped<IWorkflowProvider, WorkflowProvider>();

        // Registry Initialization Task
        services.AddTransient<IStartupTask, WorkflowRegistryInitializationTask>();
    }
}
