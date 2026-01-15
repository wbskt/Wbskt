using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Wbskt.Auth.Api.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;
using Wbskt.Auth.Api.ViewModels.Authorization;
using Wbskt.Auth.Api.Data;

namespace Wbskt.Auth.Api.Controllers;

[ApiController]
[Route("connect")]
public class AuthorizationController : Controller
{
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly AuthDbContext _context;

    public AuthorizationController(
        IOpenIddictApplicationManager applicationManager,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        AuthDbContext context)
    {
        _applicationManager = applicationManager;
        _userManager = userManager;
        _signInManager = signInManager;
        _context = context;
    }

    [HttpGet("authorize")]
    [HttpPost("authorize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Authorize()
    {
        var request = HttpContext.GetOpenIddictServerRequest() ??
            throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        // If the user is authenticated, handle the request
        var result = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!result.Succeeded || result.Principal == null)
        {
            // If the client application requested promptless authentication,
            // return an error indicating that the user is not logged in.
            if (request.HasPrompt(Prompts.None))
            {
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.LoginRequired,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The user is not logged in."
                    }));
            }

            // Return the login view
            return View("Login", new LoginViewModel { ReturnUrl = Request.Path + Request.QueryString });
        }
        
        // Since we are now authenticated (IdentityConstants.ApplicationScheme),
        // we create the claims principal for OpenIddict
        var user = await _userManager.GetUserAsync(result.Principal);
        if (user == null)
        {
             return View("Login", new LoginViewModel 
             { 
                 ReturnUrl = Request.Path + Request.QueryString,
             });
        }

        var principal = await CreateUserPrincipalAsync(user, request.GetScopes());

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }
    
    [HttpPost("authorize/login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View("Login", model);
        }

        var user = await _userManager.FindByNameAsync(model.Username);
        if (user != null)
        {
            var result = await _signInManager.PasswordSignInAsync(user, model.Password, true, false);
            if (result.Succeeded)
            {
                // Redirect back to the original URL (which was /connect/authorize?...)
                if (Url.IsLocalUrl(model.ReturnUrl))
                {
                    return Redirect(model.ReturnUrl);
                }
                return RedirectToAction("Index", "Home");
            }
        }

        ModelState.AddModelError(string.Empty, "Invalid login attempt.");
        return View("Login", model);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return SignOut(
            authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            properties: new AuthenticationProperties
            {
                RedirectUri = "/"
            });
    }

    [HttpPost("token")]
    [Produces("application/json")]
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest();
        if (request == null)
        {
            return BadRequest("The OpenID Connect request cannot be retrieved.");
        }

        if (request.IsAuthorizationCodeGrantType())
        {
             // Retrieve the claims principal stored in the authorization code
            var principal = (await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal;
            if (principal == null)
            {
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The token is no longer valid."
                    }));
            }
            
             // Validate user
            var user = await _userManager.GetUserAsync(principal);
            if (user == null || !await _signInManager.CanSignInAsync(user))
            {
                 return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The user is no longer allowed to sign in."
                    }));
            }

            // Ensure the user is still allowed to sign in.
            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        else if (request.IsPasswordGrantType())
        {
            var user = await _userManager.FindByNameAsync(request.Username ?? string.Empty);
            if (user == null)
            {
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The username/password couple is invalid."
                    }));
            }

            // Validate the password
            var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password ?? string.Empty, lockoutOnFailure: true);
            if (!result.Succeeded)
            {
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The username/password couple is invalid."
                    }));
            }

            // Create the principal
            var principal = await CreateUserPrincipalAsync(user, request.GetScopes());

            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        else if (request.IsClientCredentialsGrantType())
        {
            // Note: the client credentials are automatically validated by OpenIddict:
            // if client_id or client_secret are invalid, this action won't be invoked.

            var application = await _applicationManager.FindByClientIdAsync(request.ClientId ?? string.Empty);
            if (application == null)
            {
                throw new InvalidOperationException("The application details cannot be found in the database.");
            }

            // Create a new ClaimsIdentity containing the claims that
            // will be used to create an id_token, a token or a code.
            var identity = new ClaimsIdentity(
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                Claims.Name, Claims.Role);

            // Use the client_id as the subject identifier.
            var subjectClaim = new Claim(Claims.Subject,
                await _applicationManager.GetClientIdAsync(application) ?? throw new InvalidOperationException());
            subjectClaim.SetDestinations(Destinations.AccessToken, Destinations.IdentityToken);
            identity.AddClaim(subjectClaim);

            var nameClaim = new Claim(Claims.Name,
                await _applicationManager.GetDisplayNameAsync(application) ?? throw new InvalidOperationException());
            nameClaim.SetDestinations(Destinations.AccessToken, Destinations.IdentityToken);
            identity.AddClaim(nameClaim);

            var principal = new ClaimsPrincipal(identity);

            principal.SetScopes(request.GetScopes());

            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        else if (request.IsRefreshTokenGrantType())
        {
            // Retrieve the claims principal stored in the refresh token.
            var principal = (await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal;
            if (principal == null)
            {
                 return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The token is no longer valid."
                    }));
            }
            
            // Validate the user if the token is bound to a user
            var user = await _userManager.GetUserAsync(principal);
            if (user == null)
            {
                 return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The token is no longer valid."
                    }));
            }
            
            // Ensure the user is still allowed to sign in.
            if (!await _signInManager.CanSignInAsync(user))
            {
                 return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The user is no longer allowed to sign in."
                    }));
            }

            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        throw new NotImplementedException("The specified grant type is not implemented.");
    }
    
    private async Task<ClaimsPrincipal> CreateUserPrincipalAsync(ApplicationUser user, IEnumerable<string> scopes)
    {
        var principal = await _signInManager.CreateUserPrincipalAsync(user);

        // Set the list of scopes granted to the client application.
        principal.SetScopes(scopes);
        
        // Set the audience (Resource Server)
        principal.SetResources("wbskt_api");

        // Identity adds "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"
        // But OpenIddict needs "sub".
        if (principal.Identity is ClaimsIdentity identity)
        {
            var userId = await _userManager.GetUserIdAsync(user);
            if (!identity.HasClaim(c => c.Type == Claims.Subject))
            {
                identity.AddClaim(new Claim(Claims.Subject, userId));
            }
        }

        // --- Custom RBAC Logic ---
        // 1. Get User Roles
        var roles = await _userManager.GetRolesAsync(user);
        
        // 2. Get Role Ids
        // Note: This assumes AspNetRoles are used. We need to query Roles table.
        // But Identity stores roles by Name usually in the Principal. 
        // Let's do a direct DB lookup for permissions based on Role Names for efficiency.
        
        // Fetch RoleIds for the user's roles
        // We use _context to join Roles -> RolePermissions -> Permissions
        // IdentityRole table name is "AspNetRoles" by default but mapped to IdentityRole entity
        
        var userPermissions = from r in _context.Roles
                              join rp in _context.RolePermissions on r.Id equals rp.RoleId
                              join p in _context.Permissions on rp.PermissionId equals p.Id
                              where roles.Contains(r.Name)
                              select p.Code;

        foreach (var permCode in userPermissions.Distinct())
        {
            var claim = new Claim("permission", permCode);
            claim.SetDestinations(Destinations.AccessToken, Destinations.IdentityToken);
            ((ClaimsIdentity)principal.Identity!).AddClaim(claim);
        }
        // -------------------------

        foreach (var claim in principal.Claims)
        {
            claim.SetDestinations(GetDestinations(claim, principal));
        }

        return principal;
    }

    private IEnumerable<string> GetDestinations(Claim claim, ClaimsPrincipal principal)
    {
        // Note: by default, claims are NOT automatically included in the access and identity tokens.
        // To allow OpenIddict to serialize them, you must attach them a destination, that specifies
        // whether they should be included in access tokens, in identity tokens or in both.

        switch (claim.Type)
        {
            case Claims.Name:
                yield return Destinations.AccessToken;

                if (principal.HasScope(Scopes.Profile))
                    yield return Destinations.IdentityToken;

                yield break;

            case Claims.Email:
                yield return Destinations.AccessToken;

                if (principal.HasScope(Scopes.Email))
                    yield return Destinations.IdentityToken;

                yield break;

            case Claims.Role:
                yield return Destinations.AccessToken;

                if (principal.HasScope(Scopes.Roles))
                    yield return Destinations.IdentityToken;

                yield break;

            case Claims.Subject:
                yield return Destinations.AccessToken;
                yield return Destinations.IdentityToken;
                yield break;
            
            case "permission":
                yield return Destinations.AccessToken;
                yield return Destinations.IdentityToken;
                yield break;

            // Never include the security stamp in the access and identity tokens, as it's a secret value.
            case "AspNet.Identity.SecurityStamp": yield break;

            default:
                yield return Destinations.AccessToken;
                yield break;
        }
    }
}
