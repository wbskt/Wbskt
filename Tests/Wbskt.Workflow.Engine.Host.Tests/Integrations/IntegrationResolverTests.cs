using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Contracts;
using Wbskt.Workflow.Integrations;
using static Wbskt.Workflow.Engine.Host.Tests.Integrations.IntegrationTestKit;

namespace Wbskt.Workflow.Engine.Host.Tests.Integrations;

public sealed class IntegrationResolverTests
{
    private const int WorkspaceId = 9;
    private static readonly Guid RefId = Guid.Parse("12121212-3434-5656-7878-909090909090");

    [Fact]
    public async Task Resolves_an_integration_of_the_branchs_workspace_with_its_secret_open()
    {
        var store = new FakeStore(Row(IntegrationType.Telegram, "{}", new { botToken = BotToken }));

        var resolution = await Resolver(store).ResolveAsync(Branch(), RefId, IntegrationType.Telegram, CancellationToken.None);

        Assert.Null(resolution.Failure);
        var integration = resolution.Integration!;
        Assert.Equal(21, integration.Id);
        Assert.Equal(RefId, integration.RefId);
        Assert.Equal(BotToken, integration.Secrets.BotToken);
        Assert.IsType<TelegramSettings>(integration.Settings);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task A_step_with_no_integration_is_not_found(string? refId)
    {
        var store = new FakeStore(null);

        var resolution = await Resolver(store).ResolveAsync(Branch(), refId is null ? null : Guid.Parse(refId), IntegrationType.Telegram, CancellationToken.None);

        AssertFailed(resolution, "INTEGRATION_NOT_FOUND");
        Assert.Equal(0, store.Finds);
    }

    [Fact]
    public async Task A_deleted_integration_is_not_found()
    {
        AssertFailed(await Resolver(new FakeStore(null)).ResolveAsync(Branch(), RefId, IntegrationType.Telegram, CancellationToken.None), "INTEGRATION_NOT_FOUND");
    }

    [Fact]
    public async Task Another_workspaces_integration_is_as_absent_as_a_missing_one()
    {
        var store = new FakeStore(Row(IntegrationType.Telegram, "{}", new { botToken = BotToken }));

        var resolution = await Resolver(store).ResolveAsync(Branch(WorkspaceId + 1), RefId, IntegrationType.Telegram, CancellationToken.None);

        AssertFailed(resolution, "INTEGRATION_NOT_FOUND");
    }

    [Fact]
    public async Task An_integration_of_another_type_is_refused()
    {
        var store = new FakeStore(Row(IntegrationType.Telegram, "{}", new { botToken = BotToken }));

        var resolution = await Resolver(store).ResolveAsync(Branch(), RefId, IntegrationType.Discord, CancellationToken.None);

        AssertFailed(resolution, "INTEGRATION_WRONG_TYPE");
        Assert.Contains("telegram", resolution.Failure!.Message);
        Assert.Contains("discord", resolution.Failure.Message);
    }

    [Fact]
    public async Task Settings_that_no_longer_pass_are_invalid()
    {
        // Stored under a looser engine (dev transport), read by a production one.
        var store = new FakeStore(Row(IntegrationType.Ntfy, "{\"serverUrl\":\"http://ntfy.local\"}", new { topic = "alerts" }));

        AssertFailed(await Resolver(store).ResolveAsync(Branch(), RefId, IntegrationType.Ntfy, CancellationToken.None), "INTEGRATION_INVALID");
    }

    [Fact]
    public async Task A_secret_that_does_not_open_is_unreadable_without_saying_why()
    {
        var row = Row(IntegrationType.Telegram, "{}", new { botToken = BotToken });
        var store = new FakeStore(row with { Secret = row.Secret with { KeyId = 5 } });

        var resolution = await Resolver(store).ResolveAsync(Branch(), RefId, IntegrationType.Telegram, CancellationToken.None);

        AssertFailed(resolution, "INTEGRATION_UNREADABLE");
    }

    [Fact]
    public async Task Marking_used_stamps_the_row()
    {
        var store = new FakeStore(null);
        var integration = new ResolvedIntegration(21, RefId, WorkspaceId, IntegrationType.Telegram, null, new TelegramSettings(), IntegrationSecrets.Empty);

        await Resolver(store).MarkUsedAsync(integration, CancellationToken.None);

        Assert.Equal([21], store.Touched);
    }

    [Fact]
    public async Task A_failure_to_mark_used_never_fails_the_step()
    {
        var store = new FakeStore(null) { TouchFails = true };
        var integration = new ResolvedIntegration(21, RefId, WorkspaceId, IntegrationType.Telegram, null, new TelegramSettings(), IntegrationSecrets.Empty);

        await Resolver(store).MarkUsedAsync(integration, CancellationToken.None);
    }

    [Fact]
    public async Task A_draft_with_no_row_is_not_marked_used()
    {
        var store = new FakeStore(null);
        var draft = new ResolvedIntegration(null, RefId, WorkspaceId, IntegrationType.Telegram, null, new TelegramSettings(), IntegrationSecrets.Empty);

        await Resolver(store).MarkUsedAsync(draft, CancellationToken.None);

        Assert.Empty(store.Touched);
    }

    // ----------------------------------------------------------------- glue

    private static void AssertFailed(IntegrationResolution resolution, string code)
    {
        Assert.Null(resolution.Integration);
        Assert.Equal(code, resolution.Failure!.ErrorCode);
        Assert.False(resolution.Failure.Retryable);
    }

    private static IntegrationResolver Resolver(FakeStore store)
    {
        var options = Options();
        var vault = new IntegrationVault(Cipher(options), Microsoft.Extensions.Options.Options.Create(options), []);
        return new IntegrationResolver(store, vault, Microsoft.Extensions.Options.Options.Create(options), NullLogger<IntegrationResolver>.Instance);
    }

    private static SealedIntegrationRow Row(IntegrationType type, string configJson, object secrets)
    {
        var secret = Cipher().Seal(WorkspaceId, RefId, type, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(secrets)));
        return new SealedIntegrationRow(21, RefId, WorkspaceId, type, null, configJson, secret);
    }

    private static BranchContext Branch(int workspaceId = WorkspaceId) =>
        new(1, 2, 3, Guid.NewGuid(), 1, "node", 1, new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>(), "corr", DateTime.UtcNow, workspaceId);

    private sealed class FakeStore(SealedIntegrationRow? row) : IIntegrationStore
    {
        public int Finds { get; private set; }
        public List<int> Touched { get; } = [];
        public bool TouchFails { get; init; }

        public Task<SealedIntegrationRow?> FindSealedAsync(Guid refId, CancellationToken ct)
        {
            Finds++;
            return Task.FromResult(row?.RefId == refId ? row : null);
        }

        public Task TouchLastUsedAsync(int id, CancellationToken ct)
        {
            if (TouchFails)
            {
                throw new InvalidOperationException("database down");
            }

            Touched.Add(id);
            return Task.CompletedTask;
        }
    }
}
