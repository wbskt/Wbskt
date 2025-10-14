using Microsoft.Extensions.DependencyInjection;
using Wbskt.Common.Readers;
using Wbskt.Common.Readers.Cache;
using Wbskt.Common.Readers.Database;
using Wbskt.Common.Readers.Database.Implementation;
using Wbskt.Common.Services;
using Wbskt.Common.Writers;
using Wbskt.Common.Writers.Implementations;

namespace Wbskt.Common;

public static class DependencyInjection
{
    public static void ConfigureCommonServices(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddMemoryCache();
        serviceCollection.AddSingleton<ICacheService, CacheService>();
        serviceCollection.AddSingleton<ICancellationService, CancellationService>();
        
        serviceCollection.AddSingleton<IConnectionStringProvider, ConnectionStringProvider>();

        // Clients
        serviceCollection.AddSingleton<IClientsWriter, ClientsWriter>();
        serviceCollection.AddSingleton<IClientsDatabaseReader, ClientsDatabaseReader>();
        serviceCollection.AddSingleton<IClientsReader, CachedClientsReader>();

        // Registration Policies
        serviceCollection.AddSingleton<IRegistrationPoliciesWriter, RegistrationPoliciesWriter>();
        serviceCollection.AddSingleton<IRegistrationPoliciesDatabaseReader, RegistrationPoliciesDatabaseReader>();
        serviceCollection.AddSingleton<IRegistrationPoliciesReader, CachedRegistrationPoliciesReader>();

        // Users
        serviceCollection.AddSingleton<IUsersWriter, UsersWriter>();
        serviceCollection.AddSingleton<IUsersDatabaseReader, UsersDatabaseReader>();
        serviceCollection.AddSingleton<IUsersReader, CachedUsersReader>();

        // User Refresh Tokens
        serviceCollection.AddSingleton<IUserRefreshTokensWriter, UserRefreshTokensWriter>();
        serviceCollection.AddSingleton<IUserRefreshTokensDatabaseReader, UserRefreshTokensDatabaseReader>();
        
        // Workflow
        serviceCollection.AddScoped<IWorkflowsDatabaseReader, WorkflowsDatabaseReader>();
        serviceCollection.AddScoped<IWorkflowsReader, CachedWorkflowsReader>();
        serviceCollection.AddScoped<IWorkflowsWriter, WorkflowsWriter>();
        
        // Workflow Steps
        serviceCollection.AddScoped<IWorkflowStepsReader, CachedWorkflowStepsReader>();
        serviceCollection.AddScoped<IWorkflowStepsDatabaseReader, WorkflowStepsDatabaseReader>();
        serviceCollection.AddScoped<IWorkflowStepsWriter, WorkflowStepsWriter>();

        // Workflow Execution
        serviceCollection.AddScoped<IWorkflowExecutionsWriter, WorkflowExecutionsDatabaseWriter>();
        serviceCollection.AddScoped<IWorkflowExecutionsReader, WorkflowExecutionsDatabaseReader>();

        // Servers
        serviceCollection.AddSingleton<IServersDatabaseReader, ServersDatabaseReader>();
        serviceCollection.AddSingleton<IServersReader, CachedServersReader>();
    }
}