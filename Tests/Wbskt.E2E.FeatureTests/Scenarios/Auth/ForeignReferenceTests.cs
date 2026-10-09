using System.Net;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Auth;

/// <summary>
/// "The ID Boundary" in Docs/Coding.Conventions.md: inside a workspace the caller can use, a
/// reference to another workspace's policy or device gets the same 404 as one that names nothing,
/// whether it is in the path or a list filter. A 403 there would confirm the reference is real, and
/// an empty page would hide that the filter could never match.
///
/// Requires the auth and management hosts running. Skips gracefully when they are down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class ForeignReferenceTests(ServicesFixture fixture)
{
    private static readonly object PolicyUpdate = new { Name = "renamed", MaxClients = (int?)null, AutoApproval = true, IsEnabled = true };

    [SkippableTheory]
    [InlineData("GET", "registration-policies/{policy}", "POLICY_NOT_FOUND")]
    [InlineData("PATCH", "registration-policies/{policy}", "POLICY_NOT_FOUND")]
    [InlineData("POST", "registration-policies/{policy}/rotate-pin", "POLICY_NOT_FOUND")]
    [InlineData("POST", "registration-policies/{policy}/disable", "POLICY_NOT_FOUND")]
    [InlineData("GET", "clients?policyRefId={policy}", "POLICY_NOT_FOUND")]
    [InlineData("GET", "clients/policy/{policy}", "POLICY_NOT_FOUND")]
    [InlineData("GET", "message-templates?policyRefId={policy}", "POLICY_NOT_FOUND")]
    [InlineData("GET", "event-logs?policyRefId={policy}", "POLICY_NOT_FOUND")]
    [InlineData("GET", "event-logs?clientRefId={client}", "CLIENT_NOT_FOUND")]
    [InlineData("GET", "clients/{client}", "CLIENT_NOT_FOUND")]
    [InlineData("POST", "clients/{client}/ping", "CLIENT_NOT_FOUND")]
    public async Task ID_01_AnotherWorkspacesReference_ReadsLikeAnUnknownOne(string method, string path, string code)
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (ownerToken, ownerWorkspace) = await fixture.RegisterAndLoginUserAsync();
        var (policyRef, pin) = await fixture.CreatePolicyAsync(ownerToken, ownerWorkspace);
        var (clientRef, _) = await fixture.RegisterClientAsync(pin, "not-yours");
        var (strangerToken, strangerWorkspace) = await fixture.RegisterAndLoginUserAsync();

        var foreign = await SendAsync(method, strangerWorkspace, path, strangerToken, policyRef, clientRef);
        var unknown = await SendAsync(method, strangerWorkspace, path, strangerToken, Guid.NewGuid(), Guid.NewGuid());

        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ServicesFixture.ReadErrorCodeAsync(foreign)).Should().Be(code);
        (await ServicesFixture.ReadErrorCodeAsync(unknown)).Should().Be(code);

        // And the owner's policy is untouched by the stranger's attempts.
        var own = await fixture.SendAsync(HttpMethod.Get, Url(ownerWorkspace, $"registration-policies/{policyRef}"), ownerToken);
        own.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private Task<HttpResponseMessage> SendAsync(string method, Guid workspace, string path, string token, Guid policyRef, Guid clientRef)
    {
        var url = Url(workspace, path.Replace("{policy}", policyRef.ToString()).Replace("{client}", clientRef.ToString()));
        var body = method == "PATCH" ? PolicyUpdate : null;
        return fixture.SendAsync(new HttpMethod(method), url, token, body);
    }

    private static string Url(Guid workspace, string path) => ServicesFixture.ManagementUrl($"/api/workspaces/{workspace}/{path}");
}
