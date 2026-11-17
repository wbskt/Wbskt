using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Enums;

/// <summary>
/// Where a published workflow stands (<c>dbo.Workflows.State</c>). Only an enabled workflow starts runs,
/// and it starts them on its one live version. A deleted workflow is state 4 in the database and never
/// leaves it: every read that returns a state leaves deleted workflows out.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<WorkflowState>))]
public enum WorkflowState : byte
{
    Enabled = 1,
    Disabled = 2,
    Deprecated = 3
}
