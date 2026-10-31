using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.Host.Tests.Abstraction;

/// <summary>A Wait for signal correlation must be able to fill in to a value of its own.</summary>
public sealed class AwaitSignalValidationTests
{
    private static readonly WorkflowValidator Validator = new();

    [Theory]
    [InlineData("greenhouse")]
    [InlineData("{{trigger.payload.room}}")]
    [InlineData("room-{{trigger.payload.room}}")]
    [InlineData("{{$shared.currentRoom}}")]
    [InlineData("{{ parentRunRefId }}")]
    public void A_constant_or_a_lookup_is_accepted(string correlation)
    {
        Assert.Empty(IssuesFor(correlation));
    }

    [Theory]
    [InlineData("$trigger.room", "'{{trigger.")]
    [InlineData("trigger.payload.room}}", "brace")]
    [InlineData("{trigger.payload.room}", "brace")]
    [InlineData("{{trigger.payload.room + 1}}", "not a value it can look up")]
    [InlineData("{{$trigger.room}}", "not a value it can look up")]
    [InlineData("{{trigger..room}}", "not a value it can look up")]
    public void A_correlation_that_cannot_fill_in_is_refused(string correlation, string expected)
    {
        var issue = Assert.Single(IssuesFor(correlation));
        Assert.Equal("AWAIT_SIGNAL_CORRELATION_INVALID", issue.Code);
        Assert.Contains(expected, issue.Message);
    }

    [Fact]
    public void An_unclosed_placeholder_is_reported_once_as_a_template_error()
    {
        var issue = Assert.Single(IssuesFor("{{trigger.payload.room"));
        Assert.Equal("TEMPLATE_INVALID", issue.Code);
        Assert.Contains("correlation", issue.Message);
    }

    private static List<ValidationIssue> IssuesFor(string correlation)
    {
        var node = new AwaitSignalNode
        {
            NodeId = Guid.NewGuid(),
            Name = "wait for approval",
            Ports = [],
            Config = new AwaitSignalConfig { SignalName = "approve", Correlation = correlation }
        };
        var def = ValidWorkflowBuilder.Build();
        var result = Validator.Validate(def with { Nodes = [.. def.Nodes, node] });
        return result.Issues.Where(i => i.NodeId == node.NodeId && i.Severity == ValidationSeverity.Error).ToList();
    }
}
