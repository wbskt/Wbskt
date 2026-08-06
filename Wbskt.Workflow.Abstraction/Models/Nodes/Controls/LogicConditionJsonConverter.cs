using System.Text.Json;
using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Models.Expressions;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;

/// <summary>
/// Reads a Logic node's condition as a structured <see cref="WorkflowExpression"/>, while still
/// accepting the legacy bare-string form so definitions published before the change keep running.
///
/// A legacy string is mapped exactly the way the old executor interpreted it - "true"/"false" became a
/// literal, anything else was a branch-state path - so an existing workflow behaves identically. It is
/// always written back in structured form, so a legacy definition is upgraded the next time it is
/// republished.
/// </summary>
public sealed class LogicConditionJsonConverter : JsonConverter<WorkflowExpression>
{
    public override WorkflowExpression? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            return FromLegacyString(reader.GetString());
        }

        // Structured form - the property-level converter does not apply to this nested read, so the
        // base type's [JsonPolymorphic] discriminator handling takes over as normal.
        return JsonSerializer.Deserialize<WorkflowExpression>(ref reader, options);
    }

    public override void Write(Utf8JsonWriter writer, WorkflowExpression value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, typeof(WorkflowExpression), options);
    }

    /// <summary>
    /// The pre-structured interpretation of a condition string: a bool literal, otherwise a
    /// branch-state path. Kept public so the builder produces byte-identical definitions for callers
    /// still passing a string.
    /// </summary>
    public static WorkflowExpression FromLegacyString(string? condition)
    {
        if (bool.TryParse(condition, out bool literal))
        {
            return new LiteralExpression(literal);
        }

        return new BranchStateRefExpression(condition ?? string.Empty);
    }
}
