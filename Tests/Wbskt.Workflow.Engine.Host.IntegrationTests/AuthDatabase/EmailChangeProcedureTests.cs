using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.AuthDatabase;

/// <summary>
/// EmailChange_Confirm and EmailChange_Revert against a real SQL Server. Which tokens each one spends
/// is the whole of the feature's safety net, and the unit suite mocks the provider.
/// </summary>
[Collection("SqlEdge")]
public sealed class EmailChangeProcedureTests(AuthSqlFixture fixture)
{
    private const string Skipped = "SQL Server not reachable — skipping.";

    private static byte[] NewTokenHash() =>
        System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray());

    [SkippableFact]
    public async Task Confirming_switches_the_address_and_issues_an_undo()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);

        (int userId, string original) = await SeedUserAsync("change-happy");
        string moved = Address("moved");
        byte[] revert = NewTokenHash();

        byte[] confirm = await RequestAsync(userId, moved);
        await ConfirmAsync(confirm, revert);

        Assert.Equal(moved, await EmailOfAsync(userId));

        await RevertAsync(revert);
        Assert.Equal(original, await EmailOfAsync(userId));
    }

    /// <summary>
    /// A stolen session that changes the address twice must not take away the owner's undo from the
    /// first change, and whoever holds the second address must not be able to spend it.
    /// </summary>
    [SkippableFact]
    public async Task A_second_change_leaves_the_owners_undo_from_the_first()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);

        (int userId, string original) = await SeedUserAsync("change-chain");
        string second = Address("second");
        string third = Address("third");
        byte[] ownersUndo = NewTokenHash();
        byte[] attackersUndo = NewTokenHash();

        byte[] firstConfirm = await RequestAsync(userId, second);
        await ConfirmAsync(firstConfirm, ownersUndo);
        byte[] secondConfirm = await RequestAsync(userId, third);
        await ConfirmAsync(secondConfirm, attackersUndo);

        await RevertAsync(attackersUndo);
        Assert.Equal(second, await EmailOfAsync(userId));

        await RevertAsync(ownersUndo);
        Assert.Equal(original, await EmailOfAsync(userId));
    }

    [SkippableFact]
    public async Task Reverting_spends_the_undo_and_every_later_one()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);

        (int userId, _) = await SeedUserAsync("change-later");
        byte[] firstUndo = NewTokenHash();
        byte[] secondUndo = NewTokenHash();

        byte[] firstConfirm = await RequestAsync(userId, Address("a"));
        await ConfirmAsync(firstConfirm, firstUndo);
        byte[] secondConfirm = await RequestAsync(userId, Address("b"));
        await ConfirmAsync(secondConfirm, secondUndo);
        byte[] pending = await RequestAsync(userId, Address("c"));

        await RevertAsync(firstUndo);

        Assert.Equal(50020, (await Assert.ThrowsAsync<SqlException>(() => RevertAsync(firstUndo))).Number);
        Assert.Equal(50020, (await Assert.ThrowsAsync<SqlException>(() => RevertAsync(secondUndo))).Number);
        Assert.Equal(50020, (await Assert.ThrowsAsync<SqlException>(() => ConfirmAsync(pending, NewTokenHash()))).Number);
    }

    private static string Address(string tag) => $"{tag}-{Guid.NewGuid():N}"[..30] + "@example.test";

    private async Task<(int Id, string Email)> SeedUserAsync(string tag)
    {
        string unique = $"{tag}-{Guid.NewGuid():N}"[..30];
        string email = $"{unique}@example.test";

        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand(
            """
            INSERT INTO dbo.Users (Username, Email, PasswordHash, IsActive)
            OUTPUT INSERTED.Id
            VALUES (@Username, @Email, 'initial-hash', 1);
            """, conn);
        cmd.Parameters.AddWithValue("@Username", unique);
        cmd.Parameters.AddWithValue("@Email", email);

        return ((int)(await cmd.ExecuteScalarAsync())!, email);
    }

    private async Task<byte[]> RequestAsync(int userId, string newEmail)
    {
        byte[] hash = NewTokenHash();
        await RunAsync("dbo.EmailChangeToken_Create", cmd =>
        {
            cmd.Parameters.AddWithValue("@UserId", userId);
            cmd.Parameters.AddWithValue("@NewEmail", newEmail);
            cmd.Parameters.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = hash;
            cmd.Parameters.AddWithValue("@ExpiresAt", DateTime.UtcNow.AddHours(1));
        });
        return hash;
    }

    private Task ConfirmAsync(byte[] tokenHash, byte[] revertTokenHash) =>
        RunAsync("dbo.EmailChange_Confirm", cmd =>
        {
            cmd.Parameters.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = tokenHash;
            cmd.Parameters.Add("@RevertTokenHash", SqlDbType.VarBinary, 32).Value = revertTokenHash;
            cmd.Parameters.AddWithValue("@RevertExpiresAt", DateTime.UtcNow.AddDays(7));
            cmd.Parameters.Add("@UserId", SqlDbType.Int).Direction = ParameterDirection.Output;
            cmd.Parameters.Add("@OldEmail", SqlDbType.NVarChar, 100).Direction = ParameterDirection.Output;
            cmd.Parameters.Add("@NewEmail", SqlDbType.NVarChar, 100).Direction = ParameterDirection.Output;
        });

    private Task RevertAsync(byte[] tokenHash) =>
        RunAsync("dbo.EmailChange_Revert", cmd =>
        {
            cmd.Parameters.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = tokenHash;
            cmd.Parameters.Add("@UserId", SqlDbType.Int).Direction = ParameterDirection.Output;
            cmd.Parameters.Add("@RestoredEmail", SqlDbType.NVarChar, 100).Direction = ParameterDirection.Output;
            cmd.Parameters.Add("@AbandonedEmail", SqlDbType.NVarChar, 100).Direction = ParameterDirection.Output;
        });

    private async Task RunAsync(string procedure, Action<SqlCommand> bind)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand(procedure, conn) { CommandType = CommandType.StoredProcedure };
        bind(cmd);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<string> EmailOfAsync(int userId)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("SELECT Email FROM dbo.Users WHERE Id = @Id", conn);
        cmd.Parameters.AddWithValue("@Id", userId);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<SqlConnection> OpenAsync()
    {
        var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }
}
