using Wbskt.Workflow.Abstraction.Models.Triggers;

namespace Wbskt.Host.Tests.Abstraction;

public class CorrelationKeyExpressionTests
{
    [Theory]
    [InlineData("$trigger.payload.room", "payload.room")]
    [InlineData("$TRIGGER.payload.room", "payload.room")]
    [InlineData("{{trigger.payload.room}}", "payload.room")]
    [InlineData("{{ trigger.payload.room }}", "payload.room")]
    [InlineData("  {{trigger.messageType}}  ", "messageType")]
    public void A_payload_path_is_read_in_either_form(string expression, string expectedPath)
    {
        Assert.True(CorrelationKeyExpression.TryGetTriggerPath(expression, out string path));
        Assert.Equal(expectedPath, path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("greenhouse")]
    [InlineData("{{payload.room}}")]
    [InlineData("room-{{trigger.payload.room}}")]
    [InlineData("{{trigger.a}}-{{trigger.b}}")]
    public void Anything_else_is_not_a_payload_path(string? expression)
    {
        Assert.False(CorrelationKeyExpression.TryGetTriggerPath(expression, out _));
    }

    [Theory]
    [InlineData("{{payload.room}}", true)]
    [InlineData("room-{{trigger.payload.room}}", true)]
    [InlineData("{{trigger.payload.room}}", false)]
    [InlineData("greenhouse", false)]
    [InlineData(null, false)]
    public void Only_a_template_that_is_not_a_payload_path_is_unresolvable(string? expression, bool expected)
    {
        Assert.Equal(expected, CorrelationKeyExpression.IsUnresolvableTemplate(expression));
    }
}
