using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Wbskt.Management.Host.Services;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

public sealed class ClientSecretsTests
{
    [Fact]
    public void Generated_secrets_are_unique()
    {
        string[] secrets = Enumerable.Range(0, 200).Select(_ => ClientSecrets.Generate()).ToArray();

        secrets.Distinct().Should().HaveCount(secrets.Length);
    }

    [Fact]
    public void Generated_secrets_carry_256_bits_of_entropy()
    {
        // Base64 of 32 bytes. If this shrinks, the argument for SHA-256 over a slow KDF shrinks with
        // it — the whole reason a fast hash is safe here is that there is nothing to brute-force.
        Convert.FromBase64String(ClientSecrets.Generate()).Should().HaveCount(32);
    }

    [Fact]
    public void Hash_is_sha256_over_the_utf8_bytes()
    {
        const string secret = "a-secret-value";

        ClientSecrets.Hash(secret).Should().Equal(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    }

    [Fact]
    public void Hash_fits_the_column()
    {
        ClientSecrets.Hash(ClientSecrets.Generate()).Should().HaveCount(32);
    }

    [Fact]
    public void Matching_secret_is_accepted()
    {
        string secret = ClientSecrets.Generate();

        ClientSecrets.Matches(ClientSecrets.Hash(secret), secret).Should().BeTrue();
    }

    [Fact]
    public void Different_secret_is_rejected()
    {
        ClientSecrets.Matches(ClientSecrets.Hash(ClientSecrets.Generate()), ClientSecrets.Generate())
            .Should().BeFalse();
    }

    /// <summary>
    /// The old <c>Client_Verify</c> compared with SQL's <c>=</c> under the database's default
    /// collation, which is case-insensitive — so a secret matched regardless of case, throwing away
    /// roughly a bit of entropy per alphabetic character. Hashing makes the comparison exact, and
    /// this pins that as intended behaviour rather than an accident.
    /// </summary>
    [Fact]
    public void Comparison_is_case_sensitive()
    {
        const string secret = "AbCdEf";

        ClientSecrets.Matches(ClientSecrets.Hash(secret), "abcdef").Should().BeFalse();
        ClientSecrets.Matches(ClientSecrets.Hash(secret), "ABCDEF").Should().BeFalse();
        ClientSecrets.Matches(ClientSecrets.Hash(secret), secret).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Absent_secret_is_rejected(string? presented)
    {
        ClientSecrets.Matches(ClientSecrets.Hash("whatever"), presented).Should().BeFalse();
    }

    [Fact]
    public void Absent_stored_hash_is_rejected()
    {
        ClientSecrets.Matches(null, "whatever").Should().BeFalse();
    }

    /// <summary>
    /// A truncated or otherwise malformed stored hash must be refused, not throw:
    /// <c>FixedTimeEquals</c> is length-sensitive, and an exception here would surface as a 500 on
    /// the device login path rather than a clean rejection.
    /// </summary>
    [Fact]
    public void Wrong_length_stored_hash_is_rejected_without_throwing()
    {
        string secret = ClientSecrets.Generate();
        byte[] truncated = ClientSecrets.Hash(secret)[..16];

        ClientSecrets.Matches(truncated, secret).Should().BeFalse();
        ClientSecrets.Matches([], secret).Should().BeFalse();
    }
}
