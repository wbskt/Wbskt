using System.Text.Json.Serialization;

namespace Wbskt.Models.Billing;

/// <summary>Which of the plan's limits refused a run.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RunRefusalReason>))]
public enum RunRefusalReason
{
    /// <summary>The plan already had its limit of runs going at once.</summary>
    ActiveRunLimit,

    /// <summary>The plan had spent its workflow credits for the month.</summary>
    CreditLimit,

    /// <summary>The workspace had spent its own monthly credit cap.</summary>
    WorkspaceCreditCap
}
