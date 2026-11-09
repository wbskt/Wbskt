using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Wbskt.Models.Billing;

namespace Wbskt.Infrastructure.Billing;

public static class BillingServiceCollectionExtensions
{
    /// <summary>
    /// The plan catalogue and each workspace's plan, read from the main database's copy of its account.
    /// For the hosts that enforce plan limits on the main database.
    /// </summary>
    public static IServiceCollection AddWorkspacePlans(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.AddPlanCatalog(configuration);
        services.TryAddSingleton<IWorkspaceAccountProvider, WorkspaceAccountProvider>();
        services.TryAddSingleton<IWorkspacePlans, WorkspacePlans>();
        return services;
    }

    /// <summary>What each plan allows (<see cref="PlanCatalog"/>), with the configuration's changes to it.</summary>
    public static IServiceCollection AddPlanCatalog(this IServiceCollection services, IConfiguration configuration)
    {
        // Built here rather than on first use, so a bad limit in configuration stops the host at start.
        var options = new PlanCatalogOptions
        {
            Plans = configuration.GetSection(PlanCatalogOptions.SectionName).Get<Dictionary<PlanCode, PlanLimitOverrides>>() ?? []
        };
        services.TryAddSingleton(new PlanCatalog(Options.Create(options)));
        return services;
    }
}
