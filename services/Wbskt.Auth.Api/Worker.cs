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
        if (!_environment.IsDevelopment())
        {
            return;
        }
        
        using var scope = _serviceProvider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await context.Database.EnsureCreatedAsync(cancellationToken);

        // --- Seed Roles ---
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var roles = new[] { Constants.Roles.Admin, Constants.Roles.User };
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        // --- Seed Permissions ---
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

        // --- Assign Permissions to Admin Role ---
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
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var adminEmail = "admin@wbskt.com";
        if (await userManager.FindByEmailAsync(adminEmail) == null)
        {
            var user = new ApplicationUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true };
            var result = await userManager.CreateAsync(user, _configuration["SeedUser:Password"]);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(user, Constants.Roles.Admin);
            }
        }

        // --- Seed Scopes ---
        var scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();

        if (await scopeManager.FindByNameAsync(Scopes.Email, cancellationToken) is null)
        {
            await scopeManager.CreateAsync(new OpenIddictScopeDescriptor
            {
                Name = Scopes.Email,
                DisplayName = "Email access"
            }, cancellationToken);
        }

        if (await scopeManager.FindByNameAsync(Scopes.Profile, cancellationToken) is null)
        {
            await scopeManager.CreateAsync(new OpenIddictScopeDescriptor
            {
                Name = Scopes.Profile,
                DisplayName = "Profile access"
            }, cancellationToken);
        }

        if (await scopeManager.FindByNameAsync(Scopes.Roles, cancellationToken) is null)
        {
            await scopeManager.CreateAsync(new OpenIddictScopeDescriptor
            {
                Name = Scopes.Roles,
                DisplayName = "Roles access"
            }, cancellationToken);
        }

        if (await scopeManager.FindByNameAsync(Scopes.OfflineAccess, cancellationToken) is null)
        {
            await scopeManager.CreateAsync(new OpenIddictScopeDescriptor
            {
                Name = Scopes.OfflineAccess,
                DisplayName = "Offline access (Refresh Token)"
            }, cancellationToken);
        }

        // --- Seed Applications ---
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        if (await manager.FindByClientIdAsync(Constants.Clients.Postman, cancellationToken) is null)
        {
            await manager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = Constants.Clients.Postman,
                ClientSecret = _configuration["Postman:ClientSecret"],
                DisplayName = "Postman",
                Permissions =
                {
                    Permissions.Endpoints.Token,
                    Permissions.GrantTypes.Password,
                    Permissions.GrantTypes.ClientCredentials,
                    Permissions.GrantTypes.RefreshToken,
                    Permissions.Scopes.Email,
                    Permissions.Scopes.Profile,
                    Permissions.Scopes.Roles
                }
            }, cancellationToken);
        }

        if (await manager.FindByClientIdAsync(Constants.Clients.WbsktFrontend, cancellationToken) is null)
        {
            await manager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = Constants.Clients.WbsktFrontend,
                // No client secret for public clients (SPA) using PKCE
                DisplayName = "WBSKT Frontend",
                RedirectUris = { new Uri("https://localhost:3000/callback") },
                Permissions =
                {
                    Permissions.Endpoints.Authorization,
                    Permissions.Endpoints.Token,
                    Permissions.GrantTypes.AuthorizationCode,
                    Permissions.GrantTypes.RefreshToken,
                    Permissions.ResponseTypes.Code,
                    Permissions.Scopes.Email,
                    Permissions.Scopes.Profile,
                    Permissions.Scopes.Roles
                },
                Requirements =
                {
                    Requirements.Features.ProofKeyForCodeExchange
                }
            }, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
