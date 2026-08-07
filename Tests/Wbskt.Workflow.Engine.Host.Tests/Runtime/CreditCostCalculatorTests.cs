using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class CreditCostCalculatorTests
{
    [Fact]
    public void Outbound_work_costs_more_than_bookkeeping()
    {
        // The point of the model. Charging both 1.0 made CreditBudget a node-execution ceiling wearing
        // a billing label: a thousand local variable sets and a thousand outbound calls billed the same.
        var calculator = new DefaultCreditCostCalculator();

        decimal webhook = calculator.Calculate(Webhook(), null!);
        decimal variable = calculator.Calculate(Variable(), null!);

        Assert.True(webhook > variable, $"expected an outbound webhook ({webhook}) to cost more than a variable set ({variable})");
    }

    [Fact]
    public void Configuration_overrides_the_built_in_price()
    {
        // Pricing changes without a release, so it must not be compiled in.
        var calculator = new DefaultCreditCostCalculator(Options.Create(new CreditCostOptions
        {
            PerKind = new Dictionary<string, decimal> { [NodeKind.ActionWebhook] = 42m }
        }));

        Assert.Equal(42m, calculator.Calculate(Webhook(), null!));
    }

    [Fact]
    public void A_kind_with_no_entry_falls_back_to_the_default()
    {
        // Kinds added after the price list must still be billable rather than free.
        var calculator = new DefaultCreditCostCalculator(Options.Create(new CreditCostOptions { DefaultCost = 7m }));

        Assert.Equal(7m, calculator.Calculate(new ManualTriggerNode { NodeId = Guid.NewGuid(), Name = "t", Ports = [], Config = new Wbskt.Workflow.Abstraction.Models.Triggers.ManualTriggerConfig { Description = "d" } }, null!));
    }

    [Fact]
    public void Every_executable_kind_has_a_price()
    {
        // A kind that silently prices at the default is fine; one that prices at zero is not, because
        // it would be invisible to the budget and to the meter.
        var options = new CreditCostOptions();

        Assert.All(NodeKind.Executable, kind => Assert.True(options.CostFor(kind) > 0m, $"{kind} priced at {options.CostFor(kind)}"));
    }

    private static BaseNode Webhook() => new WebhookNotificationNode
    {
        NodeId = Guid.NewGuid(),
        Name = "call",
        Ports = [],
        Config = new WebhookNotificationConfig { Url = "https://example.test/hook", Method = "POST" }
    };

    private static BaseNode Variable() => new VariableNode
    {
        NodeId = Guid.NewGuid(),
        Name = "set",
        Ports = [],
        Config = new VariableConfig { Scope = Wbskt.Workflow.Abstraction.Enums.VariableScope.Local, Op = Wbskt.Workflow.Abstraction.Enums.VariableOperation.Set, Var = "x" }
    };
}
