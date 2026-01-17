using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using Wbskt.Auth.Api.Data;
using Wbskt.Auth.Api.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;
using Wbskt.Common;

namespace Wbskt.Auth.Api;

public class Worker : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public Worker(IServiceProvider serviceProvider, IConfiguration configuration, IHostEnvironment environment)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Seeding runs in Dev environments to ensure easy setup.
        // In Prod, this would likely be a separate migration job.
        if (!_environment.IsDevelopment())
        {
            return;
        }
        
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        
        // Ensure Database Exists
        await context.Database.EnsureCreatedAsync(cancellationToken);

        // 1. Seed Identity (Roles & Users)
        await SeedIdentityAsync(scope, context, cancellationToken);

        // 2. Seed OpenIddict Scopes
        await SeedScopesAsync(scope, cancellationToken);

        // 3. Seed OpenIddict Clients (Applications)
        await SeedClientsAsync(scope, cancellationToken);
    }

    private async Task SeedIdentityAsync(IServiceScope scope, AuthDbContext context, CancellationToken cancellationToken)
    {
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // --- Roles ---
        var roles = new[] { Constants.Roles.Admin, Constants.Roles.User };
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        // --- Permissions (Granular) ---
        var permissions = new[]
        {
            Constants.Permissions.WorkflowRead, Constants.Permissions.WorkflowWrite, Constants.Permissions.WorkflowDelete,
            Constants.Permissions.ClientRead, Constants.Permissions.ClientWrite, Constants.Permissions.ClientDelete
        };

        foreach (var code in permissions)
        {
            if (!context.Permissions.Any(p => p.Code == code))
            {
                context.Permissions.Add(new Permission { Code = code, Description = $"Allow {code}" });
            }
        }
        await context.SaveChangesAsync(cancellationToken);

        // --- Assign All Permissions to Admin Role ---
        var adminRole = await roleManager.FindByNameAsync(Constants.Roles.Admin);
        if (adminRole != null)
        {
            var allPermissions = context.Permissions.ToList();
            foreach (var perm in allPermissions)
            {
                if (!context.RolePermissions.Any(rp => rp.RoleId == adminRole.Id && rp.PermissionId == perm.Id))
                {
                    context.RolePermissions.Add(new RolePermission { RoleId = adminRole.Id, PermissionId = perm.Id });
                }
            }
            await context.SaveChangesAsync(cancellationToken);
        }

        // --- Seed Admin User ---
        var adminEmail = _configuration["SeedUser:User"]!;
        if (await userManager.FindByEmailAsync(adminEmail) == null)
        {
            var user = new ApplicationUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true };
            var result = await userManager.CreateAsync(user, _configuration["SeedUser:Password"]!);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(user, Constants.Roles.Admin);
            }
        }
    }

    private async Task SeedScopesAsync(IServiceScope scope, CancellationToken cancellationToken)
    {
        var scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();

        var scopesToCreate = new[]
        {
            (Name: Constants.Scopes.Email, Display: "Email access"),
            (Name: Constants.Scopes.Profile, Display: "Profile access"),
            (Name: Constants.Scopes.Roles, Display: "Roles access"),
            (Name: Constants.Scopes.OfflineAccess, Display: "Offline access (Refresh Token)"),
            (Name: Constants.Scopes.WbsktApi, Display: "WBSKT API Access")
        };

        foreach (var (name, display) in scopesToCreate)
        {
            if (await scopeManager.FindByNameAsync(name, cancellationToken) is null)
            {
                await scopeManager.CreateAsync(new OpenIddictScopeDescriptor
                {
                    Name = name,
                    DisplayName = display,
                    Resources = { Constants.Audiences.WbsktApi } // All these scopes relate to the WBSKT resource server
                }, cancellationToken);
            }
        }
    }

    private async Task SeedClientsAsync(IServiceScope scope, CancellationToken cancellationToken)
    {
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        // 1. Postman (Machine-to-Machine / Testing)
        await EnsureClientAsync(manager, new OpenIddictApplicationDescriptor
        {
            ClientId = Constants.Clients.Postman,
            ClientSecret = _configuration["ClientSecret:Postman"]!,
            DisplayName = nameof(Constants.Clients.Postman),
            Permissions =
            {
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.Password,
                Permissions.GrantTypes.ClientCredentials,
                Permissions.GrantTypes.RefreshToken,
                Permissions.Prefixes.Scope + Constants.Scopes.Email,
                Permissions.Prefixes.Scope + Constants.Scopes.Profile,
                Permissions.Prefixes.Scope + Constants.Scopes.Roles,
                Permissions.Prefixes.Scope + Constants.Scopes.WbsktApi // IMPORTANT: Allow testing the API
            }
        }, cancellationToken);

        // 2. Gateway BFF (The Main Entry Point)
        await EnsureClientAsync(manager, new OpenIddictApplicationDescriptor
        {
            ClientId = Constants.Clients.WbsktGateway,
            ClientSecret = _configuration["ClientSecret:WbsktGateway"]!,
            DisplayName = "WBSKT Gateway (BFF)",
            RedirectUris = { new Uri("http://localhost:5000/signin-oidc") },
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                
                Permissions.Prefixes.Scope + Constants.Scopes.Email,
                Permissions.Prefixes.Scope + Constants.Scopes.Profile,
                Permissions.Prefixes.Scope + Constants.Scopes.Roles,
                Permissions.Prefixes.Scope + Constants.Scopes.OfflineAccess,
                Permissions.Prefixes.Scope + Constants.Scopes.WbsktApi
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Helper to Upsert (Update or Insert) a client definition.
    /// This ensures the DB state always matches the code definition.
    /// </summary>
    private async Task EnsureClientAsync(IOpenIddictApplicationManager manager, OpenIddictApplicationDescriptor descriptor, CancellationToken cancellationToken)
    {
        var client = await manager.FindByClientIdAsync(descriptor.ClientId!, cancellationToken);

        if (client == null)
        {
            await manager.CreateAsync(descriptor, cancellationToken);
        }
        else
        {
            // Populate the existing client with the new descriptor values (overwrite)
            await manager.PopulateAsync(descriptor, client, cancellationToken);
            await manager.UpdateAsync(client, descriptor, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}