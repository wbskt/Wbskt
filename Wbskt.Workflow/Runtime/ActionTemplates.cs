using System.Text;
using System.Text.Json;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

/// <summary>
/// Fills the <c>{{ ... }}</c> placeholders in an action step's config from the branch, just before the
/// step sends. A placeholder with no value is sent as empty text, as in a <c>TemplateExpression</c>,
/// and a <see cref="HistoryEventKind.TemplateValueMissing"/> warning on the run names the field and
/// the placeholder, so a message that went out with a blank can be traced.
/// </summary>
/// <remarks>
/// The warning records the placeholder, never a value: <c>$shared</c> variables may hold secrets.
/// </remarks>
internal sealed class ActionTemplates(IExpressionEvaluator evaluator, IRunJournal journal)
{
    /// <summary>Free text: a message, a body, a title.</summary>
    public Task<string> TextAsync(NodeContext ctx, string field, string value, CancellationToken ct)
    {
        return RenderAsync(ctx, field, value, singleLine: false, ct);
    }

    public async Task<string?> TextOrNullAsync(NodeContext ctx, string field, string? value, CancellationToken ct)
    {
        return value is null ? null : await RenderAsync(ctx, field, value, singleLine: false, ct);
    }

    /// <summary>
    /// A value that must stay on one line: an email or HTTP header, a URL, an id. Line breaks in what a
    /// placeholder brings in become spaces, so a value cannot start a header of its own.
    /// </summary>
    public Task<string> LineAsync(NodeContext ctx, string field, string value, CancellationToken ct)
    {
        return RenderAsync(ctx, field, value, singleLine: true, ct);
    }

    public async Task<string?> LineOrNullAsync(NodeContext ctx, string field, string? value, CancellationToken ct)
    {
        return value is null ? null : await RenderAsync(ctx, field, value, singleLine: true, ct);
    }

    /// <summary>
    /// A JSON value: each string in it is filled in, and a string that is exactly one placeholder takes
    /// the value's own JSON type, so <c>{ "on": "{{heaterOn}}" }</c> sends a boolean. Property names
    /// are left as written.
    /// </summary>
    public async Task<JsonElement?> JsonAsync(NodeContext ctx, string field, JsonElement? value, CancellationToken ct)
    {
        if (value is not { } element || !TemplatePlaceholders.Contains(element.GetRawText()))
        {
            return value;
        }

        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        await using (var writer = new Utf8JsonWriter(buffer))
        {
            await WriteAsync(ctx, field, element, writer, ct);
        }

        using JsonDocument document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private async Task WriteAsync(NodeContext ctx, string path, JsonElement element, Utf8JsonWriter writer, CancellationToken ct)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    await WriteAsync(ctx, $"{path}.{property.Name}", property.Value, writer, ct);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                int index = 0;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    await WriteAsync(ctx, $"{path}[{index++}]", item, writer, ct);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.String when TemplatePlaceholders.Contains(element.GetString()):
                string text = element.GetString()!;
                if (TemplatePlaceholders.Sole(text) is { } reference)
                {
                    JsonElement resolved = await ResolveAsync(reference, ctx, ct);
                    if (IsMissing(resolved))
                    {
                        await WarnAsync(ctx, path, [reference], ct);
                        writer.WriteStringValue(string.Empty);
                    }
                    else
                    {
                        resolved.WriteTo(writer);
                    }
                }
                else
                {
                    writer.WriteStringValue(await RenderAsync(ctx, path, text, singleLine: false, ct));
                }

                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    private async Task<string> RenderAsync(NodeContext ctx, string field, string value, bool singleLine, CancellationToken ct)
    {
        if (!TemplatePlaceholders.Contains(value))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        List<string>? missing = null;
        foreach (TemplatePlaceholders.Part part in TemplatePlaceholders.Parse(value))
        {
            if (!part.IsPlaceholder)
            {
                builder.Append(part.Text);
                continue;
            }

            JsonElement resolved = await ResolveAsync(part.Text, ctx, ct);
            if (IsMissing(resolved))
            {
                (missing ??= []).Add(part.Text);
                continue;
            }

            string text = ExpressionEvaluator.ToStringValue(resolved);
            builder.Append(singleLine ? text.ReplaceLineEndings(" ") : text);
        }

        if (missing is not null)
        {
            await WarnAsync(ctx, field, missing, ct);
        }

        return builder.ToString();
    }

    private Task<JsonElement> ResolveAsync(string reference, NodeContext ctx, CancellationToken ct)
    {
        return evaluator.EvaluateAsync(TemplatePlaceholders.ToExpression(reference), ctx.Branch, ct);
    }

    private static bool IsMissing(JsonElement value) => value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;

    private Task WarnAsync(NodeContext ctx, string field, IReadOnlyList<string> placeholders, CancellationToken ct)
    {
        return journal.AppendAsync(
            new RunJournalEntry(checked((int)ctx.Branch.RunId), HistoryEventKind.TemplateValueMissing)
            {
                BranchRefId = ctx.Branch.BranchRefId,
                NodeId = ctx.Node.NodeId,
                Payload = new { field, placeholders }
            },
            ct);
    }
}
