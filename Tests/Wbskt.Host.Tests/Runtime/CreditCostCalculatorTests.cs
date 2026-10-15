using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Host.Tests.Runtime;

public sealed class CreditCostCalculatorTests
{
    [Fact]
    public void Outbound_work_costs_more_than_bookkeeping()
    {
        // The point of the model. Charging both 1.0 made CreditBudget a node-execution ceiling wearing
        // a billing label: a thousand local variable sets and a thousand outbound calls billed the same.
        var calculator = new DefaultCreditCostCalculator();

        decimal webhook = calculator.Calculate(Webhook());
        decimal variable = calculator.Calculate(Variable());

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

        Assert.Equal(42m, calculator.Calculate(Webhook()));
    }

    [Fact]
    public void A_kind_with_no_entry_falls_back_to_the_default()
    {
        // Kinds added after the price list must still be billable rather than free.
        var calculator = new DefaultCreditCostCalculator(Options.Create(new CreditCostOptions { DefaultCost = 7m }));

        Assert.Equal(7m, calculator.Calculate(new UnpricedNode { NodeId = Guid.NewGuid(), Name = "n", Ports = [] }));
    }

    [Fact]
    public void A_trigger_node_costs_nothing()
    {
        // Every run starts at its trigger node, which only hands the inbound payload on. Unpriced, it
        // fell through to the default, so every run paid a full credit before its first real step.
        var calculator = new DefaultCreditCostCalculator(Options.Create(new CreditCostOptions { DefaultCost = 7m }));

        Assert.Equal(0m, calculator.Calculate(new ManualTriggerNode { NodeId = Guid.NewGuid(), Name = "t", Ports = [], Config = new Wbskt.Workflow.Abstraction.Models.Triggers.ManualTriggerConfig { Description = "d" } }));
        Assert.Equal(0m, calculator.Calculate(new WebhookTriggerNode { NodeId = Guid.NewGuid(), Name = "t", Ports = [], Config = new Wbskt.Workflow.Abstraction.Models.Triggers.WebhookTriggerConfig { Path = "orders", Method = "POST" } }));
    }

    private sealed record UnpricedNode : BaseNode
    {
        public override string Kind => "action:notPricedYet";
    }

    [Fact]
    public void Every_executable_kind_has_a_price()
    {
        // Every non-trigger kind is priced by its own descriptor rather than falling through to the
        // default, and priced above zero, because a free kind would be invisible to the budget and to
        // the meter. Triggers are the one exception, and are free by category, not by table entry.
        var options = new CreditCostOptions { DefaultCost = 0m };

        Assert.All(NodeKinds.All.Where(descriptor => descriptor.Category != NodeCategory.Trigger), descriptor =>
        {
            Assert.True(CreditCostOptions.BuiltIn.ContainsKey(descriptor.Kind), $"{descriptor.Kind} has no built-in price");
            Assert.True(options.CostFor(descriptor.Kind) > 0m, $"{descriptor.Kind} priced at {options.CostFor(descriptor.Kind)}");
        });

        Assert.All(NodeKinds.All.Where(descriptor => descriptor.Category == NodeCategory.Trigger), descriptor =>
            Assert.False(CreditCostOptions.BuiltIn.ContainsKey(descriptor.Kind), $"{descriptor.Kind} is a trigger but is in the price list"));
    }

    [Fact]
    public void A_trigger_stays_free_even_when_configuration_prices_it()
    {
        var calculator = new DefaultCreditCostCalculator(Options.Create(new CreditCostOptions
        {
            PerKind = new Dictionary<string, decimal> { [NodeKind.TriggerManual] = 3m }
        }));

        Assert.Equal(0m, calculator.Calculate(new ManualTriggerNode { NodeId = Guid.NewGuid(), Name = "t", Ports = [], Config = new Wbskt.Workflow.Abstraction.Models.Triggers.ManualTriggerConfig { Description = "d" } }));
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
