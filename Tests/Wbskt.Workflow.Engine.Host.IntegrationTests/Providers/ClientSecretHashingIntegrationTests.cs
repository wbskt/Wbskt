using System.Text;
using Microsoft.Data.SqlClient;
using Wbskt.Management.Host.Services;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// Pins the SQL and C# halves of the device-secret hash to the same bytes.
///
/// The migration backfills <c>Clients.SecretHash</c> with
/// <c>HASHBYTES('SHA2_256', CONVERT(VARCHAR(255), Secret))</c>, and every subsequent login is
/// checked against <c>ClientSecrets.Hash</c>, which digests <c>Encoding.UTF8.GetBytes</c>. Those two
/// agree only because the secrets are base64 and therefore pure ASCII — drop the
/// <c>CONVERT(VARCHAR(255), ...)</c> and <c>HASHBYTES</c> silently hashes UTF-16LE instead.
///
/// The consequence of getting it wrong is the reason this test exists: every device on the platform
/// fails to authenticate at once, on the same deploy that drops the plaintext column needed to
/// diagnose it.
/// </summary>
[Collection("SqlEdge")]
public sealed class ClientSecretHashingIntegrationTests(SqlEdgeFixture fixture)
{
    [SkippableFact]
    public async Task Sql_backfill_hash_matches_the_hash_used_at_login()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Server is not reachable.");

        // Real generated secrets, plus the base64 edge cases: padding, and the '+' and '/' that only
        // appear in some outputs.
        string[] secrets =
        [
            ClientSecrets.Generate(),
            ClientSecrets.Generate(),
            ClientSecrets.Generate(),
            Convert.ToBase64String(Encoding.UTF8.GetBytes("a")),
            Convert.ToBase64String([0xFF, 0xFE, 0xFD]),
            Convert.ToBase64String([0x00, 0x10, 0x83, 0xFB, 0xEF])
        ];

        foreach (string secret in secrets)
        {
            byte[] fromSql = await HashInSqlAsync(secret);
            byte[] fromCode = ClientSecrets.Hash(secret);

            Assert.True(
                fromSql.SequenceEqual(fromCode),
                $"SQL and C# disagree for secret '{secret}'.\n"
                + $"  HASHBYTES : {Convert.ToHexString(fromSql)}\n"
                + $"  ClientSecrets.Hash: {Convert.ToHexString(fromCode)}");
        }
    }

    /// <summary>
    /// The negative control. Without the VARCHAR conversion the digests differ, so this proves the
    /// test above is actually sensitive to the thing it claims to guard rather than passing for some
    /// unrelated reason.
    /// </summary>
    [SkippableFact]
    public async Task Hashing_the_nvarchar_directly_does_not_match()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Server is not reachable.");

        string secret = ClientSecrets.Generate();

        byte[] utf16 = await HashInSqlAsync(secret, convertToVarChar: false);

        Assert.False(
            utf16.SequenceEqual(ClientSecrets.Hash(secret)),
            "HASHBYTES over NVARCHAR unexpectedly matched the UTF-8 digest; the guard above proves nothing.");
    }

    [SkippableFact]
    public async Task Stored_hash_is_exactly_the_column_width()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Server is not reachable.");

        byte[] hash = await HashInSqlAsync(ClientSecrets.Generate());

        // Clients.SecretHash is VARBINARY(32); a wider digest would be silently truncated on insert.
        Assert.Equal(32, hash.Length);
    }

    private async Task<byte[]> HashInSqlAsync(string secret, bool convertToVarChar = true)
    {
        string expression = convertToVarChar
            ? "HASHBYTES('SHA2_256', CONVERT(VARCHAR(255), @Secret))"
            : "HASHBYTES('SHA2_256', @Secret)";

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {expression};";
        command.Parameters.AddWithValue("@Secret", secret);

        return (byte[])(await command.ExecuteScalarAsync())!;
    }
}
