using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// Pins <c>dbo.TriggerRegistration_BackfillWebhookSecretHash</c> to <see cref="WebhookSecretHash.Of"/>.
/// An author types a webhook secret, so it need not be ASCII: a hash taken over the wrong bytes would
/// turn away every call to that webhook once the dispatcher compares the stored hash.
/// </summary>
[Collection("SqlEdge")]
public sealed class WebhookSecretHashingIntegrationTests(SqlEdgeFixture fixture)
{
    private const string Skipped = "SQL Server is not reachable.";

    [SkippableFact]
    public async Task The_backfill_writes_the_hash_the_engine_compares()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        string[] secrets = ["s3cret", "pässwörd", "密钥-🔑", new string('x', 200)];
        int definitionId = await DefinitionAsync();
        var ids = new List<(int Id, string Secret)>();
        foreach (string secret in secrets)
        {
            ids.Add((await RegistrationAsync(definitionId, secret, hash: null), secret));
        }

        int alreadyHashed = await RegistrationAsync(definitionId, "s3cret", hash: "kept");
        int open = await RegistrationAsync(definitionId, null, hash: null);

        await RunAsync("EXEC dbo.TriggerRegistration_BackfillWebhookSecretHash;", [], scalar: false);

        foreach ((int id, string secret) in ids)
        {
            Assert.Equal(WebhookSecretHash.Of(secret), await HashAsync(id));
        }

        Assert.Equal("kept", await HashAsync(alreadyHashed));
        Assert.Null(await HashAsync(open));
    }

    private async Task<int> DefinitionAsync()
    {
        var provider = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        WorkflowDefinitionRow row = await provider.InsertAsync(new WorkflowDefinitionRow
        {
            Id = 0,
            RefId = Guid.NewGuid(),
            Version = 0,
            WorkspaceId = Random.Shared.Next(1_000_000, int.MaxValue),
            Name = "webhook-hash-test",
            Description = null,
            DefinitionJson = """{"nodes":[],"edges":[]}""",
            PublishedBy = 1,
            CreatedAt = DateTime.UtcNow
        }, CancellationToken.None);
        return row.Id;
    }

    private async Task<int> RegistrationAsync(int definitionId, string? secret, string? hash) =>
        Convert.ToInt32(await RunAsync("""
            INSERT INTO dbo.TriggerRegistrations (WorkflowDefinitionId, WorkflowRefId, WorkflowVersion, TriggerNodeId, TriggerKind, TriggerKey, WebhookSecret, WebhookSecretHash)
            OUTPUT INSERTED.Id
            VALUES (@p0, NEWID(), 1, NEWID(), 'webhook', CONCAT('webhook:hash:', NEWID()), @p1, @p2);
            """, [definitionId, (object?)secret ?? DBNull.Value, (object?)hash ?? DBNull.Value], scalar: true));

    private async Task<string?> HashAsync(int registrationId) =>
        await RunAsync("SELECT WebhookSecretHash FROM dbo.TriggerRegistrations WHERE Id = @p0", [registrationId], scalar: true) as string;

    private async Task<object?> RunAsync(string sql, object[] args, bool scalar)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        for (int i = 0; i < args.Length; i++)
        {
            cmd.Parameters.AddWithValue($"@p{i}", args[i]);
        }

        return scalar ? await cmd.ExecuteScalarAsync() : await cmd.ExecuteNonQueryAsync();
    }
}
