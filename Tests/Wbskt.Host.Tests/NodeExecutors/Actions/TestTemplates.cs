using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Host.Tests.NodeExecutors.Actions;

/// <summary>The placeholder filler an action executor needs, over a real evaluator.</summary>
internal static class TestTemplates
{
    public static ActionTemplates Create(ISharedVariableProvider? sharedVariables = null)
    {
        return new ActionTemplates(new ExpressionEvaluator(TimeProvider.System, sharedVariables));
    }
}
