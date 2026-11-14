using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Wbskt.Infrastructure.StaffActions;

public static class StaffActionsServiceCollectionExtensions
{
    /// <summary>
    /// The staff audit of the database behind <paramref name="connectionStringName"/>, and the runner
    /// that writes it beside each staff change. The changes must run on that same database.
    /// </summary>
    public static IServiceCollection AddStaffActions(this IServiceCollection services, string connectionStringName)
    {
        services.AddScoped<IStaffAuditProvider>(sp => new StaffAuditProvider(sp.GetRequiredService<IConfiguration>(), connectionStringName));
        services.AddScoped<IStaffActionRunner, StaffActionRunner>();
        return services;
    }
}
