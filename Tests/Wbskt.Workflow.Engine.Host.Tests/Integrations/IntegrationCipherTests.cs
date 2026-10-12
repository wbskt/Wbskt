using System.Security.Cryptography;
using System.Text;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Engine.Contracts;
using Wbskt.Workflow.Integrations;
using static Wbskt.Workflow.Engine.Host.Tests.Integrations.IntegrationTestKit;

namespace Wbskt.Workflow.Engine.Host.Tests.Integrations;

public sealed class IntegrationCipherTests
{
    private const int WorkspaceId = 7;
    private static readonly Guid RefId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly byte[] Plaintext = Encoding.UTF8.GetBytes("{\"botToken\":\"secret\"}");

    [Fact]
    public void A_sealed_secret_opens_to_what_was_sealed()
    {
        var cipher = Cipher();

        var sealedSecret = cipher.Seal(WorkspaceId, RefId, IntegrationType.Telegram, Plaintext);

        Assert.Equal(1, sealedSecret.KeyId);
        Assert.Equal(12 + 16 + Plaintext.Length, sealedSecret.Ciphertext.Length);
        Assert.Equal(Plaintext, cipher.Open(WorkspaceId, RefId, IntegrationType.Telegram, sealedSecret));
    }

    [Fact]
    public void Each_seal_uses_a_fresh_nonce()
    {
        var cipher = Cipher();

        var first = cipher.Seal(WorkspaceId, RefId, IntegrationType.Telegram, Plaintext);
        var second = cipher.Seal(WorkspaceId, RefId, IntegrationType.Telegram, Plaintext);

        Assert.NotEqual(first.Ciphertext, second.Ciphertext);
    }

    public static TheoryData<int, Guid, IntegrationType> OtherRows => new()
    {
        { WorkspaceId + 1, RefId, IntegrationType.Telegram },
        { WorkspaceId, Guid.NewGuid(), IntegrationType.Telegram },
        { WorkspaceId, RefId, IntegrationType.Discord }
    };

    [Theory]
    [MemberData(nameof(OtherRows))]
    public void A_secret_copied_onto_another_row_does_not_open(int workspaceId, Guid refId, IntegrationType type)
    {
        var cipher = Cipher();
        var sealedSecret = cipher.Seal(WorkspaceId, RefId, IntegrationType.Telegram, Plaintext);

        Assert.ThrowsAny<CryptographicException>(() => cipher.Open(workspaceId, refId, type, sealedSecret));
    }

    [Fact]
    public void A_tampered_secret_does_not_open()
    {
        var cipher = Cipher();
        var sealedSecret = cipher.Seal(WorkspaceId, RefId, IntegrationType.Telegram, Plaintext);
        var bytes = (byte[])sealedSecret.Ciphertext.Clone();
        bytes[^1] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() => cipher.Open(WorkspaceId, RefId, IntegrationType.Telegram, sealedSecret with { Ciphertext = bytes }));
    }

    [Fact]
    public void A_secret_shorter_than_nonce_and_tag_does_not_open()
    {
        Assert.ThrowsAny<CryptographicException>(() => Cipher().Open(WorkspaceId, RefId, IntegrationType.Telegram, new SealedSecret(new byte[27], 1)));
    }

    [Fact]
    public void A_secret_sealed_under_a_retired_key_still_opens_after_rotation()
    {
        var before = Cipher(Options(1, false, ("1", Key(1))));
        var sealedSecret = before.Seal(WorkspaceId, RefId, IntegrationType.Smtp, Plaintext);

        var after = Cipher(Options(2, false, ("1", Key(1)), ("2", Key(2))));
        var resealed = after.Seal(WorkspaceId, RefId, IntegrationType.Smtp, Plaintext);

        Assert.Equal(Plaintext, after.Open(WorkspaceId, RefId, IntegrationType.Smtp, sealedSecret));
        Assert.Equal(2, resealed.KeyId);
    }

    [Fact]
    public void Opening_with_a_key_that_is_not_configured_names_the_key()
    {
        var sealedSecret = Cipher(Options(2, false, ("2", Key(2)))).Seal(WorkspaceId, RefId, IntegrationType.Ntfy, Plaintext);

        var ex = Assert.Throws<IntegrationKeyUnavailableException>(() => Cipher().Open(WorkspaceId, RefId, IntegrationType.Ntfy, sealedSecret));

        Assert.Equal(2, ex.KeyId);
    }

    [Fact]
    public void A_key_under_the_same_id_but_different_bytes_does_not_open()
    {
        var sealedSecret = Cipher(Options(1, false, ("1", Key(1)))).Seal(WorkspaceId, RefId, IntegrationType.Ntfy, Plaintext);

        Assert.ThrowsAny<CryptographicException>(() => Cipher(Options(1, false, ("1", Key(9)))).Open(WorkspaceId, RefId, IntegrationType.Ntfy, sealedSecret));
    }

    [Fact]
    public void Without_the_active_key_nothing_can_be_sealed()
    {
        // An unset environment variable binds as an empty value: not configured, not a startup failure.
        var cipher = Cipher(Options(1, false, ("1", "")));

        Assert.False(cipher.IsConfigured);
        var ex = Assert.Throws<IntegrationKeyUnavailableException>(() => cipher.Seal(WorkspaceId, RefId, IntegrationType.Telegram, Plaintext));
        Assert.Equal(1, ex.KeyId);
    }

    [Fact]
    public void An_active_key_id_with_no_key_is_not_configured()
    {
        Assert.False(Cipher(Options(3, false, ("1", Key(1)))).IsConfigured);
        Assert.True(Cipher().IsConfigured);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("256")]
    [InlineData("primary")]
    public void A_key_id_outside_1_to_255_fails_at_startup(string id)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Cipher(Options(1, false, (id, Key(1)))));

        Assert.Contains($"'{id}'", ex.Message);
    }

    [Fact]
    public void A_key_that_is_not_base64_fails_at_startup()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Cipher(Options(1, false, ("1", "not base64!"))));

        Assert.Contains("not base64", ex.Message);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    public void A_key_that_is_not_32_bytes_fails_at_startup(int length)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Cipher(Options(1, false, ("1", Convert.ToBase64String(new byte[length])))));

        Assert.Contains($"it is {length}", ex.Message);
    }
}
