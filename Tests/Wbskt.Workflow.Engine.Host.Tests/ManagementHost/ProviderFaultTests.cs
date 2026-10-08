using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Infrastructure.Middlewares;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

/// <summary>
/// "Not found" is an answer and travels as a Result; a database that fails is a fault and is not
/// dressed up as one. It propagates to <see cref="GlobalExceptionMiddleware"/>, which answers the
/// safe 500 without the fault's own text.
/// </summary>
public sealed class ProviderFaultTests
{
    private const int WorkspaceId = 7;

    [Fact]
    public async Task A_missing_client_is_a_404_result()
    {
        var provider = new Mock<IClientProvider>();
        provider.Setup(p => p.FindDetailByRefIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((ClientDetail?)null);

        var result = await Service(provider).GetDetailAsync(WorkspaceId, Guid.NewGuid());

        result.Error.Should().Be(WorkspaceOwnership.ClientNotFound);
    }

    [Fact]
    public async Task A_database_fault_is_a_500_not_a_404()
    {
        var provider = new Mock<IClientProvider>();
        provider.Setup(p => p.FindDetailByRefIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Execution Timeout Expired. SELECT * FROM dbo.Client"));
        var service = Service(provider);
        var middleware = new GlobalExceptionMiddleware(
            async _ => await service.GetDetailAsync(WorkspaceId, Guid.NewGuid()),
            NullLogger<GlobalExceptionMiddleware>.Instance);
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        await middleware.InvokeAsync(context, Mock.Of<IEventBus>());

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        var body = Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
        body.Should().NotContain("SELECT").And.NotContain("Timeout");
    }

    [Fact]
    public async Task The_500_is_still_answered_when_announcing_the_fault_fails()
    {
        var bus = new Mock<IEventBus>();
        bus.Setup(b => b.PublishAsync(It.IsAny<Wbskt.Events.System.SystemErrorEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("broker down"));
        var middleware = new GlobalExceptionMiddleware(_ => throw new TimeoutException("db"), NullLogger<GlobalExceptionMiddleware>.Instance);
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        await middleware.InvokeAsync(context, bus.Object);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    private static ClientService Service(Mock<IClientProvider> provider)
    {
        return new ClientService(provider.Object, Mock.Of<IEventBus>(), Mock.Of<IClientTokenCutoffs>(), NullLogger<ClientService>.Instance);
    }
}
