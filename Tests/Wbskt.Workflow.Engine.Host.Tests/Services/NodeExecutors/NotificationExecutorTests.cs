using Microsoft.Extensions.Logging;
using Moq;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Engine.Host.Models;
using Wbskt.Workflow.Engine.Host.Services.NodeExecutors;
using ExecutionContext = Wbskt.Workflow.Engine.Host.Models.ExecutionContext;

namespace Wbskt.Workflow.Engine.Host.Tests.Services.NodeExecutors;

public class NotificationExecutorTests
{
    [Fact]
    public async Task EmailNotificationExecutor_LogsAndReturnsOutPort()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<EmailNotificationExecutor>>();
        var executor = new EmailNotificationExecutor(loggerMock.Object);
        
        var node = new EmailNotificationNode
        {
            NodeId = Guid.NewGuid(),
            Recipient = "test@example.com",
            Subject = "Test Subject",
            Body = "Test Body"
        };
        var context = new ExecutionContext(new WorkflowInstance(), Guid.NewGuid());

        // Act
        var result = await executor.ExecuteAsync(node, context);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(PortNames.Out, result.ActivatedPortIds.First());
    }
    
    [Fact]
    public async Task TelegramNotificationExecutor_LogsAndReturnsOutPort()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<TelegramNotificationExecutor>>();
        var executor = new TelegramNotificationExecutor(loggerMock.Object);
        
        var node = new TelegramNotificationNode
        {
            NodeId = Guid.NewGuid(),
            ChatId = "12345",
            Message = "Test Message"
        };
        var context = new ExecutionContext(new WorkflowInstance(), Guid.NewGuid());

        // Act
        var result = await executor.ExecuteAsync(node, context);

        Assert.True(result.IsSuccess);
        Assert.Equal(PortNames.Out, result.ActivatedPortIds.First());
    }
    
    [Fact]
    public async Task ToastNotificationExecutor_LogsAndReturnsOutPort()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<ToastNotificationExecutor>>();
        var executor = new ToastNotificationExecutor(loggerMock.Object);
        
        var node = new ToastNotificationNode
        {
            NodeId = Guid.NewGuid(),
            Title = "Warning",
            Message = "Test Toast"
        };
        var context = new ExecutionContext(new WorkflowInstance(), Guid.NewGuid());

        // Act
        var result = await executor.ExecuteAsync(node, context);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(PortNames.Out, result.ActivatedPortIds.First());
    }
}
