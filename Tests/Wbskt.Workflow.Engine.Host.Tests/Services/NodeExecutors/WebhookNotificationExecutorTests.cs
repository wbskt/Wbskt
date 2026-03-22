using System.Net;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Engine.Host.Models;
using Wbskt.Workflow.Engine.Host.Services.NodeExecutors;
using ExecutionContext = Wbskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Wbskt.Workflow.Engine.Host.Tests.Services.NodeExecutors;

public class WebhookNotificationExecutorTests
{
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly WebhookNotificationExecutor _executor;

    public WebhookNotificationExecutorTests()
    {
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        var loggerMock = new Mock<ILogger<WebhookNotificationExecutor>>();
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();

        var client = new HttpClient(_httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri("http://dummy-base")
        };

        httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(client);

        _executor = new WebhookNotificationExecutor(httpClientFactoryMock.Object, loggerMock.Object);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyUrl_ReturnsFail()
    {
        // Arrange
        var node = new WebhookNotificationNode
        {
            NodeId = Guid.NewGuid(),
            Url = ""
        };
        var context = new ExecutionContext(new WorkflowInstance(), Guid.NewGuid());

        // Act
        var result = await _executor.ExecuteAsync(node, context);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Webhook URL is empty.", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_ValidRequest_SendsHttpAndReturnsOnResponsePort()
    {
        // Arrange
        var node = new WebhookNotificationNode
        {
            NodeId = Guid.NewGuid(),
            Url = "https://example.com/api/webhook",
            Method = "POST",
            Payload = "{\"test\": true}"
        };
        var context = new ExecutionContext(new WorkflowInstance(), Guid.NewGuid());

        // Setup mock to return a 200 OK
        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        var result = await _executor.ExecuteAsync(node, context);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(PortNames.OnResponse, result.ActivatedPortIds.First());

        // Verify that SendAsync was called once
        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req => 
                req.Method == HttpMethod.Post && 
                req.RequestUri != null &&
                req.RequestUri.ToString() == "https://example.com/api/webhook"),
            ItExpr.IsAny<CancellationToken>()
        );
    }

    [Fact]
    public async Task ExecuteAsync_HttpRequestThrows_ReturnsFail()
    {
        // Arrange
        var node = new WebhookNotificationNode
        {
            NodeId = Guid.NewGuid(),
            Url = "https://example.com/api/webhook"
        };
        var context = new ExecutionContext(new WorkflowInstance(), Guid.NewGuid());

        // Setup mock to throw an exception
        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ThrowsAsync(new HttpRequestException("Server down"));

        // Act
        var result = await _executor.ExecuteAsync(node, context);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Webhook failed: Server down", result.ErrorMessage);
    }
}
