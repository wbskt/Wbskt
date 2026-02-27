namespace Webskt.Workflow.Abstraction.Models;

public class SendCommandActionNode : BaseAction
{
    public Guid TargetClientRefId { get; set; }
    public string ActionName { get; set; } = string.Empty;
    public string PayloadTemplate { get; set; } = string.Empty;
}

public class ToastNotificationNode : BaseAction
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class EmailNotificationNode : BaseAction
{
    public string Recipient { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}

public class TelegramNotificationNode : BaseAction
{
    public string ChatId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class WebhookNotificationNode : BaseAction
{
    public string Url { get; set; } = string.Empty;
    public string Method { get; set; } = "POST";
    public string Payload { get; set; } = string.Empty;
}
