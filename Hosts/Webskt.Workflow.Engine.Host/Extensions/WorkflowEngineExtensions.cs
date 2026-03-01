using Webskt.Workflow.Engine.Host.Interfaces;
using Webskt.Workflow.Engine.Host.Services;

namespace Webskt.Workflow.Engine.Host.Extensions;

public static class WorkflowEngineExtensions
{
    public static IServiceCollection AddWorkflowEngine(this IServiceCollection services)
    {
        services.AddSingleton<IWorkflowRuntimeRegistry, WorkflowRuntimeRegistry>();
        services.AddScoped<IWorkflowEngine, WorkflowEngine>();
        
        return services;
    }
}
