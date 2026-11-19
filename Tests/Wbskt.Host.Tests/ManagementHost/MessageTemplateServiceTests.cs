using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Templates;
using Wbskt.Host.Tests.AuthHost;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Models;

namespace Wbskt.Host.Tests.ManagementHost;

/// <summary>Saving, changing and deleting a message template: the audit trail, the limit and the unique name.</summary>
public sealed class MessageTemplateServiceTests
{
    private const int WorkspaceId = 7;

    private readonly Mock<IMessageTemplateProvider> _templates = new();
    private readonly Mock<IEventBus> _bus = new();

    [Fact]
    public async Task Saving_one_is_audited()
    {
        var saved = Template("Lamp on");
        _templates.Setup(p => p.InsertAsync(WorkspaceId, null, "Lamp on", "lamp", "{}", MessageTemplateService.DefaultMaxPerWorkspace, It.IsAny<CancellationToken>()))
            .ReturnsAsync(saved);

        var result = await Service().CreateAsync(WorkspaceId, Request("  Lamp on "));

        Assert.True(result.IsSuccess);
        _bus.Verify(b => b.PublishAsync(It.Is<MessageTemplateCreatedEvent>(e => e.TemplateRefId == saved.RefId && e.Name == "Lamp on"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_name_the_workspace_already_uses_is_a_conflict()
    {
        _templates.Setup(p => p.InsertAsync(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(SqlExceptions.WithNumber(2627, "duplicate"));

        var result = await Service().CreateAsync(WorkspaceId, Request("Lamp on"));

        Assert.Equal("TEMPLATE_NAME_TAKEN", result.Error.Code);
        _bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task One_over_the_configured_limit_is_refused()
    {
        _templates.Setup(p => p.InsertAsync(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), 3, It.IsAny<CancellationToken>()))
            .ThrowsAsync(SqlExceptions.WithNumber(50045, "limit"));

        var result = await Service(maxPerWorkspace: 3).CreateAsync(WorkspaceId, Request("Lamp on"));

        Assert.Equal("TEMPLATE_LIMIT_REACHED", result.Error.Code);
        Assert.Contains("at most 3", result.Error.Message);
    }

    [Fact]
    public async Task A_change_returns_the_template_and_audits_what_changed()
    {
        var before = Template("Lamp on");
        var after = Template("Lamp off", before.RefId);
        _templates.SetupSequence(p => p.FindByRefIdAsync(before.RefId, It.IsAny<CancellationToken>())).ReturnsAsync(before).ReturnsAsync(after);

        var result = await Service().UpdateAsync(WorkspaceId, before.RefId, Request("Lamp off"));

        Assert.Equal("Lamp off", result.Value.Name);
        _bus.Verify(b => b.PublishAsync(
            It.Is<MessageTemplateUpdatedEvent>(e => e.Changes!.Count == 1 && e.Changes[0].Field == "name" && e.Changes[0].Before == "Lamp on" && e.Changes[0].After == "Lamp off"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Saving_it_unchanged_is_not_audited()
    {
        var template = Template("Lamp on");
        _templates.Setup(p => p.FindByRefIdAsync(template.RefId, It.IsAny<CancellationToken>())).ReturnsAsync(template);

        var result = await Service().UpdateAsync(WorkspaceId, template.RefId, Request("Lamp on"));

        Assert.True(result.IsSuccess);
        _bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Deleting_one_is_audited()
    {
        var template = Template("Lamp on");
        _templates.Setup(p => p.FindByRefIdAsync(template.RefId, It.IsAny<CancellationToken>())).ReturnsAsync(template);

        var result = await Service().DeleteAsync(WorkspaceId, template.RefId);

        Assert.True(result.IsSuccess);
        _bus.Verify(b => b.PublishAsync(It.Is<MessageTemplateDeletedEvent>(e => e.TemplateRefId == template.RefId && e.Name == "Lamp on"), It.IsAny<CancellationToken>()), Times.Once);
    }

    private MessageTemplateService Service(int? maxPerWorkspace = null) => new(
        _templates.Object,
        Mock.Of<IRegistrationPolicyProvider>(),
        _bus.Object,
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Templates:MaxPerWorkspace"] = maxPerWorkspace?.ToString() })
            .Build(),
        NullLogger<MessageTemplateService>.Instance);

    private static MessageTemplateRequest Request(string name) => new(name, "lamp", "{}", null);

    private static MessageTemplate Template(string name, Guid? refId = null) => new()
    {
        Id = 3,
        RefId = refId ?? Guid.NewGuid(),
        WorkspaceId = WorkspaceId,
        Name = name,
        MessageType = "lamp",
        PayloadJson = "{}"
    };
}
