using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services;
using Wbskt.Models;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

public sealed class ClientServiceTagTests
{
    private const int WorkspaceId = 7;
    private const int ClientId = 42;
    private static readonly Guid ClientRefId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly Mock<IClientProvider> _provider = new();
    private readonly ClientService _service;

    public ClientServiceTagTests()
    {
        _provider.Setup(x => x.GetDetailByRefIdAsync(ClientRefId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientDetail { Id = ClientId, RefId = ClientRefId, WorkspaceId = WorkspaceId, Name = "client" });
        _provider.Setup(x => x.SetTagsAsync(ClientId, WorkspaceId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _service = new ClientService(_provider.Object, Mock.Of<IEventBus>(), Mock.Of<IClientTokenCutoffs>(), NullLogger<ClientService>.Instance);
    }

    [Theory]
    [InlineData("garage", "garage")]
    [InlineData("  Garage ", "garage")]
    [InlineData("living room", "living room")]
    [InlineData("rack-2.top_shelf", "rack-2.top_shelf")]
    [InlineData("Küche", "küche")]
    public void A_valid_tag_normalises_to_trimmed_lower_case(string raw, string expected)
    {
        ClientTags.TryNormalize(raw, out var tag).Should().BeTrue();
        tag.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a,b")]
    [InlineData("-garage")]
    [InlineData("garage-")]
    [InlineData("gar/age")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456")] // 33 characters
    public void An_invalid_tag_is_refused(string? raw)
    {
        ClientTags.TryNormalize(raw, out _).Should().BeFalse();
    }

    [Fact]
    public async Task Setting_tags_stores_them_normalised_distinct_and_sorted()
    {
        var result = await _service.SetTagsAsync(WorkspaceId, ClientRefId, ["Greenhouse", "garage", " GARAGE "]);

        result.IsSuccess.Should().BeTrue();
        result.Value.ClientRefId.Should().Be(ClientRefId);
        result.Value.Tags.Should().Equal("garage", "greenhouse");
        _provider.Verify(x => x.SetTagsAsync(ClientId, WorkspaceId,
            It.Is<IReadOnlyCollection<string>>(t => t.SequenceEqual(new[] { "garage", "greenhouse" })),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_empty_list_clears_the_tags()
    {
        var result = await _service.SetTagsAsync(WorkspaceId, ClientRefId, []);

        result.IsSuccess.Should().BeTrue();
        result.Value.Tags.Should().BeEmpty();
        _provider.Verify(x => x.SetTagsAsync(ClientId, WorkspaceId, It.Is<IReadOnlyCollection<string>>(t => t.Count == 0), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task One_invalid_tag_refuses_the_whole_set_before_any_lookup()
    {
        var result = await _service.SetTagsAsync(WorkspaceId, ClientRefId, ["garage", "a,b"]);

        result.Error.Code.Should().Be("CLIENT_TAG_INVALID");
        _provider.Verify(x => x.GetDetailByRefIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _provider.Verify(x => x.SetTagsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_missing_tag_list_is_invalid()
    {
        var result = await _service.SetTagsAsync(WorkspaceId, ClientRefId, null);

        result.Error.Code.Should().Be("CLIENT_TAG_INVALID");
    }

    [Fact]
    public async Task More_than_the_limit_of_distinct_tags_is_refused_but_duplicates_do_not_count()
    {
        var eleven = Enumerable.Range(0, ClientTags.MaxPerClient + 1).Select(i => $"t{i}").ToList();
        var tenWithRepeats = Enumerable.Range(0, ClientTags.MaxPerClient).Select(i => $"t{i}").Concat(["T0", "t1 "]).ToList();

        (await _service.SetTagsAsync(WorkspaceId, ClientRefId, eleven)).Error.Code.Should().Be("CLIENT_TAGS_TOO_MANY");
        (await _service.SetTagsAsync(WorkspaceId, ClientRefId, tenWithRepeats)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Tagging_an_unknown_client_is_not_found_and_another_workspaces_is_forbidden()
    {
        var unknown = Guid.NewGuid();
        _provider.Setup(x => x.GetDetailByRefIdAsync(unknown, It.IsAny<CancellationToken>())).ThrowsAsync(new NotFoundException("no rows"));

        (await _service.SetTagsAsync(WorkspaceId, unknown, ["garage"])).Error.Code.Should().Be("CLIENT_NOT_FOUND");
        (await _service.SetTagsAsync(WorkspaceId + 1, ClientRefId, ["garage"])).Error.Code.Should().Be("CLIENT_UNAUTHORIZED");
        _provider.Verify(x => x.SetTagsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_list_filters_by_the_normalised_tag_and_returns_each_clients_tags()
    {
        var client = new ClientDetail { RefId = ClientRefId, Name = "client", Tags = ["garage", "greenhouse"] };
        _provider.Setup(x => x.GetAllAsync(WorkspaceId, null, null, "garage", 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Wbskt.Management.Host.Models.Client>([client], 1));

        var result = await _service.GetAllAsync(WorkspaceId, null, null, " Garage", 0, 100);

        result.IsSuccess.Should().BeTrue();
        result.Value.Single().Tags.Should().Equal("garage", "greenhouse");
    }

    [Fact]
    public async Task An_invalid_tag_filter_is_refused_without_querying()
    {
        var all = await _service.GetAllAsync(WorkspaceId, null, null, "a,b", 0, 100);
        var byPolicy = await _service.GetByPolicyIdAsync(WorkspaceId, 1, null, null, "", 0, 100);

        all.Error.Code.Should().Be("CLIENT_TAG_INVALID");
        byPolicy.Error.Code.Should().Be("CLIENT_TAG_INVALID");
        _provider.Verify(x => x.GetAllAsync(It.IsAny<int>(), It.IsAny<ClientStatus?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_workspace_tag_list_carries_each_tags_client_count()
    {
        _provider.Setup(x => x.GetTagsAsync(WorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ClientTagCount("garage", 2), new ClientTagCount("greenhouse", 1)]);

        var result = await _service.GetTagsAsync(WorkspaceId);

        result.Value.Should().Equal(new ClientTagCountResponse("garage", 2), new ClientTagCountResponse("greenhouse", 1));
    }
}
