using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Integrations;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Host.Services.Integrations;
using Wbskt.Management.Models;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Engine.Contracts;
using Wbskt.Workflow.Engine.Host.Tests.AuthHost;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

/// <summary>
/// Managing a workspace's integrations. The engine seals and tests; what matters here is that its
/// answer reaches the caller, that the database's refusals read as the right conflicts, and that a
/// test is recorded on the integration only when it tested what is saved.
/// </summary>
public sealed class IntegrationServiceTests
{
    private const int WorkspaceId = 5;
    private static readonly DateTime SavedAt = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    // --------------------------------------------------------------- create

    [Fact]
    public async Task Creating_seals_under_a_new_ref_and_stores_what_the_engine_returned()
    {
        var harness = new Harness();
        IntegrationSealRequest? sent = null;
        Integration? inserted = null;
        harness.Engine.Setup(e => e.SealIntegrationAsync(It.IsAny<IntegrationSealRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IntegrationSealRequest, CancellationToken>((r, _) => sent = r)
            .ReturnsAsync(Sealed("{\"serverUrl\":\"https://ntfy.sh\"}", "••••", changed: true));
        harness.Provider.Setup(p => p.InsertAsync(It.IsAny<Integration>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<Integration, int, CancellationToken>((i, _, _) => inserted = i)
            .Returns(Task.CompletedTask);
        harness.Provider.Setup(p => p.FindByRefIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => inserted);

        var result = await harness.Service.CreateAsync(WorkspaceId, new IntegrationCreateRequest("ntfy", null, "  Phone  ", null, Json("{\"topic\":\"alerts\"}")), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(WorkspaceId, sent!.WorkspaceId);
        Assert.Null(sent.Existing);
        Assert.Equal(sent.RefId, inserted!.RefId);
        Assert.Equal("Phone", inserted.Name);
        Assert.Equal(IntegrationType.Ntfy, inserted.Type);
        Assert.Equal("{\"serverUrl\":\"https://ntfy.sh\"}", inserted.ConfigJson);
        Assert.Equal([9, 9, 9], inserted.SecretCiphertext);
        Assert.Equal(2, inserted.KeyId);
        Assert.Equal(42, inserted.CreatedBy);
        Assert.Equal(sent.RefId, result.Value.RefId);
        Assert.Equal("push", result.Value.Category);
        harness.Provider.Verify(p => p.InsertAsync(It.IsAny<Integration>(), IntegrationService.DefaultMaxPerWorkspace, It.IsAny<CancellationToken>()));
        harness.Bus.Verify(b => b.PublishAsync(It.Is<IntegrationCreatedEvent>(e => e.IntegrationRefId == sent.RefId && e.Type == "ntfy" && e.Name == "Phone"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("", "ntfy", null, "INTEGRATION_NAME_INVALID")]
    [InlineData("two\nlines", "ntfy", null, "INTEGRATION_NAME_INVALID")]
    [InlineData("Phone", "Ntfy", null, "INTEGRATION_TYPE_INVALID")]
    [InlineData("Phone", "telegram", "homeAssistant", "INTEGRATION_PRESET_INVALID")]
    [InlineData("Phone", "httpAuth", "grafana", "INTEGRATION_PRESET_INVALID")]
    public async Task Creating_refuses_bad_input_before_asking_the_engine(string name, string type, string? preset, string code)
    {
        var harness = new Harness();

        var result = await harness.Service.CreateAsync(WorkspaceId, new IntegrationCreateRequest(type, preset, name, null, null), CancellationToken.None);

        Assert.Equal(code, result.Error.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        harness.Engine.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_name_over_100_characters_is_refused()
    {
        var result = await new Harness().Service.CreateAsync(WorkspaceId, new IntegrationCreateRequest("ntfy", null, new string('a', 101), null, null), CancellationToken.None);

        Assert.Equal("INTEGRATION_NAME_INVALID", result.Error.Code);
    }

    [Theory]
    [InlineData("INTEGRATION_SECRET_INVALID", ErrorType.Validation)]
    [InlineData("INTEGRATIONS_NOT_CONFIGURED", ErrorType.Unavailable)]
    [InlineData("INTEGRATIONS_UNAVAILABLE", ErrorType.Unavailable)]
    public async Task The_engines_refusal_to_seal_reaches_the_caller_and_nothing_is_stored(string code, ErrorType type)
    {
        var harness = new Harness();
        var error = new Error(code, "engine says no", type);
        harness.Engine.Setup(e => e.SealIntegrationAsync(It.IsAny<IntegrationSealRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IntegrationSealResponse>.Failure(error));

        var result = await harness.Service.CreateAsync(WorkspaceId, new IntegrationCreateRequest("ntfy", null, "Phone", null, null), CancellationToken.None);

        Assert.Same(error, result.Error);
        harness.Provider.Verify(p => p.InsertAsync(It.IsAny<Integration>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        harness.Bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task The_workspace_limit_is_a_conflict_that_names_the_configured_limit()
    {
        var harness = new Harness(maxPerWorkspace: 3);
        harness.Engine.Setup(e => e.SealIntegrationAsync(It.IsAny<IntegrationSealRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(Sealed("{}"));
        harness.Provider.Setup(p => p.InsertAsync(It.IsAny<Integration>(), 3, It.IsAny<CancellationToken>()))
            .ThrowsAsync(SqlExceptions.WithNumber(50031, "limit"));

        var result = await harness.Service.CreateAsync(WorkspaceId, new IntegrationCreateRequest("ntfy", null, "Phone", null, null), CancellationToken.None);

        Assert.Equal("INTEGRATION_LIMIT_REACHED", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Contains("at most 3", result.Error.Message);
        harness.Bus.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(2627)]
    [InlineData(2601)]
    public async Task A_taken_name_is_a_conflict(int number)
    {
        var harness = new Harness();
        harness.Engine.Setup(e => e.SealIntegrationAsync(It.IsAny<IntegrationSealRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(Sealed("{}"));
        harness.Provider.Setup(p => p.InsertAsync(It.IsAny<Integration>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(SqlExceptions.WithNumber(number, "duplicate"));

        var result = await harness.Service.CreateAsync(WorkspaceId, new IntegrationCreateRequest("ntfy", null, "Phone", null, null), CancellationToken.None);

        Assert.Equal("INTEGRATION_NAME_TAKEN", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }

    // --------------------------------------------------------------- update

    [Fact]
    public async Task Another_workspaces_integration_is_not_found()
    {
        var harness = new Harness();
        var saved = Saved();
        harness.Provider.Setup(p => p.FindByRefIdAsync(saved.RefId, It.IsAny<CancellationToken>())).ReturnsAsync(saved);

        var result = await harness.Service.UpdateAsync(WorkspaceId + 1, saved.RefId, new IntegrationUpdateRequest("New", null, null, null), CancellationToken.None);

        Assert.Equal("INTEGRATION_NOT_FOUND", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task A_type_cannot_change()
    {
        var harness = new Harness();
        var saved = harness.Save(Saved());

        var result = await harness.Service.UpdateAsync(WorkspaceId, saved.RefId, new IntegrationUpdateRequest(null, "telegram", null, null), CancellationToken.None);

        Assert.Equal("INTEGRATION_TYPE_IMMUTABLE", result.Error.Code);
        harness.Engine.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Repeating_the_type_is_not_a_change()
    {
        var harness = new Harness();
        var saved = harness.Save(Saved());

        var result = await harness.Service.UpdateAsync(WorkspaceId, saved.RefId, new IntegrationUpdateRequest("Phone 2", "ntfy", null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task A_rename_does_not_reseal()
    {
        var harness = new Harness();
        var saved = harness.Save(Saved());

        await harness.Service.UpdateAsync(WorkspaceId, saved.RefId, new IntegrationUpdateRequest(" Phone 2 ", null, null, null), CancellationToken.None);

        harness.Engine.VerifyNoOtherCalls();
        harness.Provider.Verify(p => p.UpdateAsync(WorkspaceId, saved.Id, "Phone 2", saved.ConfigJson, false, null, null, null, false, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        harness.Bus.Verify(b => b.PublishAsync(
            It.Is<IntegrationUpdatedEvent>(e => e.Changes!.Single() == new FieldChange("name", "Phone", "Phone 2")),
            It.IsAny<CancellationToken>()), Times.Once);
        harness.Bus.Verify(b => b.PublishAsync(It.IsAny<IntegrationSecretRotatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_secrets_only_change_reseals_over_the_saved_settings_and_secret()
    {
        var harness = new Harness();
        var saved = harness.Save(Saved());
        IntegrationSealRequest? sent = null;
        harness.Engine.Setup(e => e.SealIntegrationAsync(It.IsAny<IntegrationSealRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IntegrationSealRequest, CancellationToken>((r, _) => sent = r)
            .ReturnsAsync(Sealed(saved.ConfigJson, "••••abcd", changed: true));

        await harness.Service.UpdateAsync(WorkspaceId, saved.RefId, new IntegrationUpdateRequest(null, null, null, Json("{\"accessToken\":\"tk_new\"}")), CancellationToken.None);

        Assert.Equal(saved.RefId, sent!.RefId);
        Assert.Equal("ntfy", sent.Type);
        Assert.Equal("https://ntfy.sh", sent.Config!.Value.GetProperty("serverUrl").GetString());
        Assert.Equal("tk_new", sent.Secrets!.Value.GetProperty("accessToken").GetString());
        Assert.Equal(saved.SecretCiphertext, sent.Existing!.Ciphertext);
        Assert.Equal(saved.KeyId, sent.Existing.KeyId);
        Assert.Equal("https://ntfy.sh", sent.ExistingConfig!.Value.GetProperty("serverUrl").GetString());
        harness.Provider.Verify(p => p.UpdateAsync(WorkspaceId, saved.Id, "Phone", saved.ConfigJson, false, It.Is<byte[]>(c => c.SequenceEqual(new byte[] { 9, 9, 9 })), (byte)2, "••••abcd", true, SavedAt, It.IsAny<CancellationToken>()), Times.Once);
        harness.Bus.Verify(b => b.PublishAsync(
            It.Is<IntegrationSecretRotatedEvent>(e => e.Changes!.Single() == new FieldChange("secretHint", "••••", "••••abcd")),
            It.IsAny<CancellationToken>()), Times.Once);
        harness.Bus.Verify(b => b.PublishAsync(It.IsAny<IntegrationUpdatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_edit_that_raced_another_is_refused_and_announced_to_nobody()
    {
        var harness = new Harness();
        var saved = harness.Save(Saved());
        harness.Provider.Setup(p => p.UpdateAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<byte[]?>(), It.IsAny<byte?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await harness.Service.UpdateAsync(WorkspaceId, saved.RefId, new IntegrationUpdateRequest("Renamed", null, null, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("INTEGRATION_CHANGED", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        harness.Bus.Verify(b => b.PublishAsync(It.IsAny<IntegrationUpdatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_test_of_a_saved_integration_sends_the_settings_its_secret_was_saved_with()
    {
        var harness = new Harness();
        var saved = harness.Save(Saved());
        IntegrationProbeRequest? sent = null;
        harness.Engine.Setup(e => e.ProbeIntegrationAsync(It.IsAny<IntegrationProbeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IntegrationProbeRequest, CancellationToken>((r, _) => sent = r)
            .ReturnsAsync(Result<IntegrationProbeResponse>.Success(new IntegrationProbeResponse(true, null, null, null)));

        await harness.Service.TestAsync(WorkspaceId, new IntegrationTestRequest(saved.RefId, null, null, Json("{\"serverUrl\":\"https://other.example\"}"), null, null), CancellationToken.None);

        Assert.Equal("https://other.example", sent!.Config!.Value.GetProperty("serverUrl").GetString());
        Assert.Equal("https://ntfy.sh", sent.ExistingConfig!.Value.GetProperty("serverUrl").GetString());
    }

    [Fact]
    public async Task A_reseal_that_changed_nothing_is_saved_but_not_announced()
    {
        var harness = new Harness();
        var saved = harness.Save(Saved());
        harness.Engine.Setup(e => e.SealIntegrationAsync(It.IsAny<IntegrationSealRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sealed(saved.ConfigJson, "••••", changed: false));

        await harness.Service.UpdateAsync(WorkspaceId, saved.RefId, new IntegrationUpdateRequest(null, null, null, Json("{}")), CancellationToken.None);

        harness.Provider.Verify(p => p.UpdateAsync(WorkspaceId, saved.Id, "Phone", saved.ConfigJson, false, It.IsAny<byte[]>(), (byte)2, "••••", false, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        harness.Bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_settings_change_lists_the_fields_that_changed()
    {
        var harness = new Harness();
        var saved = harness.Save(Saved());
        harness.Engine.Setup(e => e.SealIntegrationAsync(It.IsAny<IntegrationSealRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sealed("{\"serverUrl\":\"https://ntfy.example.test\"}", "••••", changed: false));

        await harness.Service.UpdateAsync(WorkspaceId, saved.RefId, new IntegrationUpdateRequest(null, null, Json("{\"serverUrl\":\"https://ntfy.example.test/\"}"), null), CancellationToken.None);

        harness.Provider.Verify(p => p.UpdateAsync(WorkspaceId, saved.Id, "Phone", "{\"serverUrl\":\"https://ntfy.example.test\"}", true, It.IsAny<byte[]>(), It.IsAny<byte?>(), It.IsAny<string?>(), false, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        harness.Bus.Verify(b => b.PublishAsync(
            It.Is<IntegrationUpdatedEvent>(e => e.Changes!.Single() == new FieldChange("config.serverUrl", "https://ntfy.sh", "https://ntfy.example.test")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_refused_reseal_changes_nothing()
    {
        var harness = new Harness();
        var saved = harness.Save(Saved());
        harness.Engine.Setup(e => e.SealIntegrationAsync(It.IsAny<IntegrationSealRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IntegrationSealResponse>.Failure(Error.Validation("INTEGRATION_SECRET_UNREADABLE", "Enter it again.")));

        var result = await harness.Service.UpdateAsync(WorkspaceId, saved.RefId, new IntegrationUpdateRequest("Renamed", null, null, Json("{}")), CancellationToken.None);

        Assert.Equal("INTEGRATION_SECRET_UNREADABLE", result.Error.Code);
        harness.Provider.Verify(p => p.UpdateAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<byte[]?>(), It.IsAny<byte?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        harness.Bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Renaming_onto_a_taken_name_is_a_conflict()
    {
        var harness = new Harness();
        var saved = harness.Save(Saved());
        harness.Provider.Setup(p => p.UpdateAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<byte[]?>(), It.IsAny<byte?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(SqlExceptions.WithNumber(2627, "duplicate"));

        var result = await harness.Service.UpdateAsync(WorkspaceId, saved.RefId, new IntegrationUpdateRequest("Taken", null, null, null), CancellationToken.None);

        Assert.Equal("INTEGRATION_NAME_TAKEN", result.Error.Code);
        harness.Bus.VerifyNoOtherCalls();
    }

    // --------------------------------------------------------------- delete

    [Fact]
    public async Task Deleting_one_in_use_is_refused_with_the_steps_in_the_way()
    {
        var harness = new Harness();
        var saved = harness.Save(Saved());
        Guid workflow = Guid.NewGuid(), step1 = Guid.NewGuid(), step2 = Guid.NewGuid();
        harness.Provider.Setup(p => p.DeleteAsync(WorkspaceId, saved.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new IntegrationUse(workflow, "Pumps", step1, "notify", true), new IntegrationUse(workflow, "Pumps", step2, "notify again", true)]);

        var result = await harness.Service.DeleteAsync(WorkspaceId, saved.RefId, CancellationToken.None);

        Assert.Equal("INTEGRATION_IN_USE", result.Result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Result.Error.Type);
        Assert.Contains("used by 1 published workflow.", result.Result.Error.Message);
        Assert.Equal([step1, step2], result.UsedBy.Select(u => u.StepId));
        Assert.All(result.UsedBy, u => Assert.Equal("published", u.Status));
        harness.Bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Deleting_one_nothing_uses_announces_it()
    {
        var harness = new Harness();
        var saved = harness.Save(Saved());
        harness.Provider.Setup(p => p.DeleteAsync(WorkspaceId, saved.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var result = await harness.Service.DeleteAsync(WorkspaceId, saved.RefId, CancellationToken.None);

        Assert.True(result.Result.IsSuccess);
        Assert.Empty(result.UsedBy);
        harness.Bus.Verify(b => b.PublishAsync(It.Is<IntegrationDeletedEvent>(e => e.IntegrationRefId == saved.RefId && e.Type == "ntfy"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Deleting_another_workspaces_integration_is_not_found_and_deletes_nothing()
    {
        var harness = new Harness();
        var saved = harness.Save(Saved(workspaceId: WorkspaceId + 1));

        var result = await harness.Service.DeleteAsync(WorkspaceId, saved.RefId, CancellationToken.None);

        Assert.Equal("INTEGRATION_NOT_FOUND", result.Result.Error.Code);
        harness.Provider.Verify(p => p.DeleteAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Usage_reports_deprecated_steps_as_such()
    {
        var harness = new Harness();
        var saved = harness.Save(Saved());
        harness.Provider.Setup(p => p.GetUsageAsync(saved.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new IntegrationUse(Guid.NewGuid(), "Old", Guid.NewGuid(), "notify", false)]);

        var result = await harness.Service.GetUsageAsync(WorkspaceId, saved.RefId, CancellationToken.None);

        Assert.Equal("deprecated", Assert.Single(result.Value.UsedBy).Status);
    }

    [Fact]
    public async Task Listing_by_an_unknown_type_is_refused()
    {
        var result = await new Harness().Service.GetAllAsync(WorkspaceId, "slack", CancellationToken.None);

        Assert.Equal("INTEGRATION_TYPE_INVALID", result.Error.Code);
    }

    // ----------------------------------------------------------------- test

    [Fact]
    public async Task Testing_what_is_saved_records_the_outcome_and_announces_it()
    {
        var harness = new Harness();
        var saved = harness.Save(Saved());
        IntegrationProbeRequest? sent = null;
        harness.Engine.Setup(e => e.ProbeIntegrationAsync(It.IsAny<IntegrationProbeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IntegrationProbeRequest, CancellationToken>((r, _) => sent = r)
            .ReturnsAsync(Result<IntegrationProbeResponse>.Success(new IntegrationProbeResponse(true, null, null, Json("{\"botUsername\":\"b\"}"))));
        harness.Provider.Setup(p => p.RecordTestAsync(saved.Id, SavedAt, true, null, null, "{\"botUsername\":\"b\"}", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await harness.Service.TestAsync(WorkspaceId, new IntegrationTestRequest(saved.RefId, null, null, null, null, new IntegrationTestOptionsDto(null, "me@example.test")), CancellationToken.None);

        Assert.True(result.Value.Ok);
        Assert.Equal(saved.RefId, sent!.RefId);
        Assert.Equal("ntfy", sent.Type);
        Assert.Equal(saved.SecretCiphertext, sent.Existing!.Ciphertext);
        Assert.Equal("https://ntfy.sh", sent.Config!.Value.GetProperty("serverUrl").GetString());
        Assert.Equal("me@example.test", sent.Options!.SendTo);
        harness.Provider.Verify(p => p.RecordTestAsync(saved.Id, SavedAt, true, null, null, "{\"botUsername\":\"b\"}", It.IsAny<CancellationToken>()), Times.Once);
        harness.Bus.Verify(b => b.PublishAsync(It.Is<IntegrationTestedEvent>(e => e.Ok && e.IntegrationRefId == saved.RefId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_failed_test_of_what_is_saved_is_recorded_without_metadata()
    {
        var harness = new Harness();
        var saved = harness.Save(Saved());
        harness.Engine.Setup(e => e.ProbeIntegrationAsync(It.IsAny<IntegrationProbeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IntegrationProbeResponse>.Success(new IntegrationProbeResponse(false, "INTEGRATION_AUTH_FAILED", "refused", Json("{\"x\":1}"))));
        harness.Provider.Setup(p => p.RecordTestAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await harness.Service.TestAsync(WorkspaceId, new IntegrationTestRequest(saved.RefId, null, null, null, null, null), CancellationToken.None);

        // A failed test is still a successful answer.
        Assert.False(result.Value.Ok);
        Assert.Equal("INTEGRATION_AUTH_FAILED", result.Value.Code);
        harness.Provider.Verify(p => p.RecordTestAsync(saved.Id, SavedAt, false, "INTEGRATION_AUTH_FAILED", "refused", null, It.IsAny<CancellationToken>()), Times.Once);
        harness.Bus.Verify(b => b.PublishAsync(It.Is<IntegrationTestedEvent>(e => !e.Ok && e.Code == "INTEGRATION_AUTH_FAILED"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_test_that_raced_an_edit_is_not_announced()
    {
        var harness = new Harness();
        var saved = harness.Save(Saved());
        harness.Engine.Setup(e => e.ProbeIntegrationAsync(It.IsAny<IntegrationProbeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IntegrationProbeResponse>.Success(new IntegrationProbeResponse(true, null, null, null)));
        harness.Provider.Setup(p => p.RecordTestAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await harness.Service.TestAsync(WorkspaceId, new IntegrationTestRequest(saved.RefId, null, null, null, null, null), CancellationToken.None);

        Assert.True(result.Value.Ok);
        harness.Bus.VerifyNoOtherCalls();
    }

    public static TheoryData<string?, string?> UnsavedEdits => new()
    {
        { "{\"serverUrl\":\"https://ntfy.example.test\"}", null },
        { null, "{\"accessToken\":\"tk\"}" },
        { "{}", "{}" }
    };

    [Theory]
    [MemberData(nameof(UnsavedEdits))]
    public async Task Testing_unsaved_edits_records_nothing(string? config, string? secrets)
    {
        var harness = new Harness();
        var saved = harness.Save(Saved());
        IntegrationProbeRequest? sent = null;
        harness.Engine.Setup(e => e.ProbeIntegrationAsync(It.IsAny<IntegrationProbeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IntegrationProbeRequest, CancellationToken>((r, _) => sent = r)
            .ReturnsAsync(Result<IntegrationProbeResponse>.Success(new IntegrationProbeResponse(true, null, null, null)));

        var result = await harness.Service.TestAsync(WorkspaceId,
            new IntegrationTestRequest(saved.RefId, null, null, config is null ? null : Json(config), secrets is null ? null : Json(secrets), null), CancellationToken.None);

        Assert.True(result.Value.Ok);
        Assert.NotNull(sent!.Existing);
        if (config is not null)
        {
            Assert.Equal(config, sent.Config!.Value.GetRawText());
        }

        harness.Provider.Verify(p => p.RecordTestAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        harness.Bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Testing_a_draft_sends_nothing_saved_and_records_nothing()
    {
        var harness = new Harness();
        IntegrationProbeRequest? sent = null;
        harness.Engine.Setup(e => e.ProbeIntegrationAsync(It.IsAny<IntegrationProbeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IntegrationProbeRequest, CancellationToken>((r, _) => sent = r)
            .ReturnsAsync(Result<IntegrationProbeResponse>.Success(new IntegrationProbeResponse(true, null, null, null)));

        var result = await harness.Service.TestAsync(WorkspaceId, new IntegrationTestRequest(null, "telegram", null, null, Json("{\"botToken\":\"t\"}"), new IntegrationTestOptionsDto("42", null)), CancellationToken.None);

        Assert.True(result.Value.Ok);
        Assert.Null(sent!.Existing);
        Assert.NotEqual(Guid.Empty, sent.RefId);
        Assert.Equal("telegram", sent.Type);
        Assert.Equal("42", sent.Options!.ChatId);
        harness.Provider.VerifyNoOtherCalls();
        harness.Bus.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("slack")]
    public async Task A_draft_needs_a_known_type(string? type)
    {
        var harness = new Harness();

        var result = await harness.Service.TestAsync(WorkspaceId, new IntegrationTestRequest(null, type, null, null, null, null), CancellationToken.None);

        Assert.Equal("INTEGRATION_TYPE_INVALID", result.Error.Code);
        harness.Engine.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Testing_a_saved_integration_as_another_type_is_refused()
    {
        var harness = new Harness();
        var saved = harness.Save(Saved());

        var result = await harness.Service.TestAsync(WorkspaceId, new IntegrationTestRequest(saved.RefId, "telegram", null, null, null, null), CancellationToken.None);

        Assert.Equal("INTEGRATION_TYPE_IMMUTABLE", result.Error.Code);
        harness.Engine.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Testing_uses_the_saved_preset_not_the_requests()
    {
        var harness = new Harness();
        var saved = harness.Save(Saved(type: IntegrationType.HttpAuth, preset: IntegrationPreset.HomeAssistant, configJson: "{\"baseUrl\":\"https://ha.example.test\",\"mode\":\"bearer\"}"));
        IntegrationProbeRequest? sent = null;
        harness.Engine.Setup(e => e.ProbeIntegrationAsync(It.IsAny<IntegrationProbeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IntegrationProbeRequest, CancellationToken>((r, _) => sent = r)
            .ReturnsAsync(Result<IntegrationProbeResponse>.Failure(Error.Unavailable("INTEGRATIONS_UNAVAILABLE", "down")));

        var result = await harness.Service.TestAsync(WorkspaceId, new IntegrationTestRequest(saved.RefId, null, "other", null, null, null), CancellationToken.None);

        Assert.Equal("homeAssistant", sent!.Preset);
        Assert.Equal("INTEGRATIONS_UNAVAILABLE", result.Error.Code);
        harness.Provider.Verify(p => p.RecordTestAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ----------------------------------------------------------------- glue

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static Result<IntegrationSealResponse> Sealed(string configJson, string? hint = "••••", bool changed = true) =>
        Result<IntegrationSealResponse>.Success(new IntegrationSealResponse(Json(configJson), new SealedSecret([9, 9, 9], 2), hint, changed));

    private static Integration Saved(int workspaceId = WorkspaceId, IntegrationType type = IntegrationType.Ntfy, IntegrationPreset? preset = null, string configJson = "{\"serverUrl\":\"https://ntfy.sh\"}") => new()
    {
        Id = 31,
        RefId = Guid.NewGuid(),
        WorkspaceId = workspaceId,
        Type = type,
        Preset = preset,
        Name = "Phone",
        ConfigJson = configJson,
        SecretCiphertext = [1, 1, 1],
        KeyId = 1,
        SecretHint = "••••",
        UpdatedAt = SavedAt
    };

    private sealed class Harness
    {
        public Harness(int? maxPerWorkspace = null)
        {
            var identity = new Mock<IIdentityService>();
            identity.Setup(i => i.GetUserIdentity()).Returns(new UserIdentity(42));
            Provider.Setup(p => p.UpdateAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<byte[]?>(), It.IsAny<byte?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            var settings = new Dictionary<string, string?>();
            if (maxPerWorkspace is { } max)
            {
                settings["Integrations:MaxPerWorkspace"] = max.ToString();
            }

            Service = new IntegrationService(
                Provider.Object,
                Engine.Object,
                identity.Object,
                Bus.Object,
                new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
                NullLogger<IntegrationService>.Instance);
        }

        public Mock<IIntegrationProvider> Provider { get; } = new();
        public Mock<IWorkflowEngineClient> Engine { get; } = new();
        public Mock<IEventBus> Bus { get; } = new();
        public IntegrationService Service { get; }

        public Integration Save(Integration integration)
        {
            Provider.Setup(p => p.FindByRefIdAsync(integration.RefId, It.IsAny<CancellationToken>())).ReturnsAsync(integration);
            return integration;
        }
    }
}
