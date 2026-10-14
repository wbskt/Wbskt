using Moq;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services.Integrations;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Host.Tests.Abstraction;

namespace Wbskt.Host.Tests.ManagementHost;

/// <summary>The half of validating a definition's integration steps that needs the workspace.</summary>
public sealed class IntegrationReferenceCheckTests
{
    private const int WorkspaceId = 6;

    private static readonly Integration Bot = Owned(IntegrationType.Telegram, "Ops bot", "{}");
    private static readonly Integration HomeAssistant = Owned(IntegrationType.HttpAuth, "Home", "{\"baseUrl\":\"https://ha.example.test:8123\",\"mode\":\"bearer\"}");
    private static readonly Integration ApiKey = Owned(IntegrationType.HttpAuth, "API", "{\"baseUrl\":\"https://api.example.test\",\"mode\":\"header\",\"headerName\":\"X-Api-Key\"}");

    [Fact]
    public async Task A_definition_with_no_integration_steps_reads_nothing()
    {
        var provider = new Mock<IIntegrationProvider>(MockBehavior.Strict);

        var result = await new IntegrationReferenceCheck(provider.Object).CheckAsync(WorkspaceId, ValidWorkflowBuilder.Build(), CancellationToken.None);

        Assert.Empty(result.Issues);
        Assert.Empty(result.Refs);
    }

    [Fact]
    public async Task A_null_definition_or_steps_without_a_ref_read_nothing()
    {
        var provider = new Mock<IIntegrationProvider>(MockBehavior.Strict);
        var check = new IntegrationReferenceCheck(provider.Object);

        Assert.Empty((await check.CheckAsync(WorkspaceId, null, CancellationToken.None)).Refs);
        Assert.Empty((await check.CheckAsync(WorkspaceId, With(Telegram(null), Webhook("https://x.test", Guid.Empty)), CancellationToken.None)).Refs);
    }

    [Fact]
    public async Task Each_step_with_an_owned_integration_of_its_type_is_recorded_and_the_workspace_is_read_once()
    {
        var provider = Provider(Bot, HomeAssistant);
        var telegram = Telegram(Bot.RefId);
        var webhook = Webhook("api/states", HomeAssistant.RefId);
        var second = Telegram(Bot.RefId);

        var result = await new IntegrationReferenceCheck(provider.Object).CheckAsync(WorkspaceId, With(telegram, webhook, second), CancellationToken.None);

        Assert.Empty(result.Issues);
        Assert.Equal(
            [(telegram.NodeId, Bot.RefId), (webhook.NodeId, HomeAssistant.RefId), (second.NodeId, Bot.RefId)],
            result.Refs.Select(r => (r.NodeId, r.IntegrationRefId)));
        provider.Verify(p => p.GetAllAsync(WorkspaceId, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_integration_the_workspace_does_not_have_is_not_found()
    {
        var node = Telegram(Guid.NewGuid());

        var result = await new IntegrationReferenceCheck(Provider(Bot).Object).CheckAsync(WorkspaceId, With(node), CancellationToken.None);

        var issue = Assert.Single(result.Issues);
        Assert.Equal("INTEGRATION_NOT_FOUND", issue.Code);
        Assert.Equal(node.NodeId, issue.NodeId);
        Assert.Empty(result.Refs);
    }

    [Fact]
    public async Task An_integration_of_another_type_is_refused()
    {
        var node = Discord(Bot.RefId);

        var result = await new IntegrationReferenceCheck(Provider(Bot).Object).CheckAsync(WorkspaceId, With(node), CancellationToken.None);

        var issue = Assert.Single(result.Issues);
        Assert.Equal("INTEGRATION_WRONG_TYPE", issue.Code);
        Assert.Contains("'Ops bot' is a telegram integration", issue.Message);
        Assert.Empty(result.Refs);
    }

    [Theory]
    [InlineData("api/states")]
    [InlineData("/api/states")]
    [InlineData("https://ha.example.test:8123/api/states")]
    public async Task Call_a_url_on_the_integrations_origin_passes(string url)
    {
        var result = await new IntegrationReferenceCheck(Provider(HomeAssistant).Object).CheckAsync(WorkspaceId, With(Webhook(url, HomeAssistant.RefId)), CancellationToken.None);

        Assert.Empty(result.Issues);
        Assert.Single(result.Refs);
    }

    [Theory]
    [InlineData("https://evil.test/api")]
    [InlineData("https://ha.example.test/api")]
    [InlineData("http://ha.example.test:8123/api")]
    public async Task Call_a_url_off_the_integrations_origin_is_refused(string url)
    {
        var result = await new IntegrationReferenceCheck(Provider(HomeAssistant).Object).CheckAsync(WorkspaceId, With(Webhook(url, HomeAssistant.RefId)), CancellationToken.None);

        Assert.Equal("WEBHOOK_ORIGIN_MISMATCH", Assert.Single(result.Issues).Code);
    }

    [Theory]
    [InlineData("bearer", "authorization", true)]
    [InlineData("bearer", "X-Api-Key", false)]
    [InlineData("header", "x-api-key", true)]
    [InlineData("header", "Authorization", false)]
    public async Task Call_a_url_may_not_set_the_header_the_credential_goes_in(string mode, string header, bool conflict)
    {
        var integration = mode == "bearer" ? HomeAssistant : ApiKey;
        var node = Webhook("status", integration.RefId, new Dictionary<string, string> { [header] = "v" });

        var result = await new IntegrationReferenceCheck(Provider(integration).Object).CheckAsync(WorkspaceId, With(node), CancellationToken.None);

        Assert.Equal(conflict, result.Issues.Any(i => i.Code == "WEBHOOK_HEADER_CONFLICT"));
    }

    // ----------------------------------------------------------------- glue

    private static Integration Owned(IntegrationType type, string name, string configJson) => new()
    {
        Id = Random.Shared.Next(1, 1000),
        RefId = Guid.NewGuid(),
        WorkspaceId = WorkspaceId,
        Type = type,
        Name = name,
        ConfigJson = configJson
    };

    private static Mock<IIntegrationProvider> Provider(params Integration[] owned)
    {
        var provider = new Mock<IIntegrationProvider>();
        provider.Setup(p => p.GetAllAsync(WorkspaceId, null, It.IsAny<CancellationToken>())).ReturnsAsync(owned);
        return provider;
    }

    private static WorkflowDefinition With(params BaseNode[] nodes)
    {
        var def = ValidWorkflowBuilder.Build();
        return def with { Nodes = [.. def.Nodes, .. nodes] };
    }

    private static TelegramNotificationNode Telegram(Guid? integrationRef) => new()
    {
        NodeId = Guid.NewGuid(),
        Name = "tg",
        Ports = [],
        Config = new TelegramConfig { IntegrationRef = integrationRef, ChatId = "1", Message = "m" }
    };

    private static DiscordNotificationNode Discord(Guid? integrationRef) => new()
    {
        NodeId = Guid.NewGuid(),
        Name = "discord",
        Ports = [],
        Config = new DiscordConfig { IntegrationRef = integrationRef, Message = "m" }
    };

    private static WebhookNotificationNode Webhook(string url, Guid? integrationRef, Dictionary<string, string>? headers = null) => new()
    {
        NodeId = Guid.NewGuid(),
        Name = "call",
        Ports = [],
        Config = new WebhookNotificationConfig { Url = url, Method = "GET", IntegrationRef = integrationRef, Headers = headers }
    };
}
