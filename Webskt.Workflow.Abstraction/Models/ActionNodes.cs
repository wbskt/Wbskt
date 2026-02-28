using Webskt.Workflow.Abstraction.Enums;

namespace Webskt.Workflow.Abstraction.Models;

public class SendCommandActionNode : BaseAction
{
    public SendCommandActionNode()
    {
        Ports.Add(new PortDefinition { PortId = "in", Direction = PortDirection.In });
        Ports.Add(new PortDefinition { PortId = "out", Direction = PortDirection.Out });
    }

    public Guid TargetClientRefId { get; set; }
    public string ActionName { get; set; } = string.Empty;
    public string PayloadTemplate { get; set; } = string.Empty;
}

public class ToastNotificationNode : BaseAction
{
    public ToastNotificationNode()
    {
        Ports.Add(new PortDefinition { PortId = "in", Direction = PortDirection.In });
        Ports.Add(new PortDefinition { PortId = "out", Direction = PortDirection.Out });
    }

    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class EmailNotificationNode : BaseAction
{
    public EmailNotificationNode()
    {
        Ports.Add(new PortDefinition { PortId = "in", Direction = PortDirection.In });
        Ports.Add(new PortDefinition { PortId = "out", Direction = PortDirection.Out });
    }

    public string Recipient { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}

public class TelegramNotificationNode : BaseAction
{
    public TelegramNotificationNode()
    {
        Ports.Add(new PortDefinition { PortId = "in", Direction = PortDirection.In });
        Ports.Add(new PortDefinition { PortId = "out", Direction = PortDirection.Out });
    }

    public string ChatId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class WebhookNotificationNode : BaseAction
{
    public WebhookNotificationNode()
    {
        Ports.Add(new PortDefinition { PortId = "in", Direction = PortDirection.In });
        Ports.Add(new PortDefinition { PortId = "out", Direction = PortDirection.Out });
    }

    public string Url { get; set; } = string.Empty;
    public string Method { get; set; } = "POST";
    public string Payload { get; set; } = string.Empty;
}
