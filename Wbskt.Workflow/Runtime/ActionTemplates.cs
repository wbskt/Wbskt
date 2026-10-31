using System.Buffers;
using System.Text;
using System.Text.Json;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Abstraction.Templates;

namespace Wbskt.Workflow.Runtime;

/// <summary>
/// Fills the <c>{{ ... }}</c> placeholders in an action step's <see cref="TemplatedAttribute">templated</see>
/// config fields from the branch, just before the step sends. A placeholder with no value is sent as
/// empty text, as in a <c>TemplateExpression</c>, and the step gets a
/// <see cref="HistoryEventKind.TemplateValueMissing"/> warning naming the field and the placeholder, so
/// a message that went out with a blank can be traced.
/// </summary>
/// <remarks>
/// The warning records the placeholder, never a value: <c>$shared</c> variables may hold secrets.
/// </remarks>
internal sealed class ActionTemplates(IExpressionEvaluator evaluator)
{
    public async Task<T> FillAsync<T>(NodeContext ctx, T config, CancellationToken ct) where T : class
    {
        return await TemplateFields.FillAsync(
            config,
            async (path, value, text) => await RenderAsync(ctx, path, value, text == TemplateText.Line, ct),
            async (path, value) => await FillJsonAsync(ctx, path, value, ct));
    }

    // Each string is filled in; a string that is exactly one placeholder takes the value's own JSON
    // type, so { "on": "{{heaterOn}}" } sends a boolean. Property names are left as written.
    private async Task<JsonElement> FillJsonAsync(NodeContext ctx, string path, JsonElement value, CancellationToken ct)
    {
        if (!TemplatePlaceholders.Contains(value.GetRawText()))
        {
            return value;
        }

        var buffer = new ArrayBufferWriter<byte>();
        await using (var writer = new Utf8JsonWriter(buffer))
        {
            await WriteAsync(ctx, path, value, writer, ct);
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
                        Warn(ctx, path, [reference]);
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

    /// <summary>
    /// The value with its placeholders filled in as one line, and the placeholders that had no value.
    /// For a key a step matches on, where a blank left in the text would match the wrong thing, so the
    /// caller decides what a missing value means instead of getting a warning.
    /// </summary>
    public async Task<(string Text, IReadOnlyList<string> Missing)> FillLineAsync(NodeContext ctx, string value, CancellationToken ct)
    {
        (string text, List<string>? missing) = await RenderPartsAsync(ctx, value, singleLine: true, ct);
        return (text, missing ?? []);
    }

    private async Task<string> RenderAsync(NodeContext ctx, string field, string value, bool singleLine, CancellationToken ct)
    {
        (string text, List<string>? missing) = await RenderPartsAsync(ctx, value, singleLine, ct);
        if (missing is not null)
        {
            Warn(ctx, field, missing);
        }

        return text;
    }

    private async Task<(string Text, List<string>? Missing)> RenderPartsAsync(NodeContext ctx, string value, bool singleLine, CancellationToken ct)
    {
        if (!TemplatePlaceholders.Contains(value))
        {
            return (value, null);
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
            // Line breaks a value brings into a header, URL or id become spaces, so it cannot start a header of its own.
            builder.Append(singleLine ? text.ReplaceLineEndings(" ") : text);
        }

        return (builder.ToString(), missing);
    }

    private Task<JsonElement> ResolveAsync(string reference, NodeContext ctx, CancellationToken ct)
    {
        return evaluator.EvaluateAsync(TemplatePlaceholders.ToExpression(reference), ctx.Branch, ct);
    }

    private static bool IsMissing(JsonElement value) => value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;

    private static void Warn(NodeContext ctx, string field, IReadOnlyList<string> placeholders)
    {
        ctx.Warnings.Add(new NodeWarning(HistoryEventKind.TemplateValueMissing, new { field, placeholders }));
    }
}
