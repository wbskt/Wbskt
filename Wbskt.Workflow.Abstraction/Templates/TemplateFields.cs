using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Templates;

/// <summary>
/// Finds and fills the <see cref="TemplatedAttribute">templated</see> properties of a step config, so
/// which fields take placeholders is declared once, on the config, and the engine and the validator
/// both read it from there. A field's path is its JSON name, as an author wrote it:
/// <c>embeds[0].fields[1].value</c>, <c>headers.X-Trace</c>, <c>payload.on</c>.
/// </summary>
public static class TemplateFields
{
    public delegate ValueTask<string> FillText(string path, string value, TemplateText text);

    public delegate ValueTask<JsonElement> FillJson(string path, JsonElement value);

    public readonly record struct Field(string Path, string Value);

    private enum Shape { Text, TextList, TextMap, Json, Record, RecordList }

    private sealed record Member(PropertyInfo Property, string Name, Shape Shape, TemplateText Text);

    private static readonly ConcurrentDictionary<Type, Member[]> Plans = new();

    /// <summary>Every templated text in the config, strings inside JSON values included.</summary>
    public static IEnumerable<Field> Find(object? config) => config is null ? [] : FindIn(config, string.Empty);

    /// <summary>
    /// A copy of the config with every templated text replaced by what <paramref name="text"/> and
    /// <paramref name="json"/> return. A config with no placeholder is returned as it is.
    /// </summary>
    public static async ValueTask<T> FillAsync<T>(T config, FillText text, FillJson json) where T : class
    {
        if (!Find(config).Any(field => TemplatePlaceholders.Contains(field.Value)))
        {
            return config;
        }

        return (T)await FillObjectAsync(config, string.Empty, text, json);
    }

    private static IEnumerable<Field> FindIn(object target, string prefix)
    {
        foreach (Member member in PlanFor(target.GetType()))
        {
            object? value = member.Property.GetValue(target);
            string path = prefix + member.Name;
            switch (member.Shape, value)
            {
                case (_, null):
                    break;
                case (Shape.Text, string text):
                    yield return new Field(path, text);
                    break;
                case (Shape.TextList, IReadOnlyList<string> list):
                    for (int i = 0; i < list.Count; i++)
                    {
                        yield return new Field($"{path}[{i}]", list[i]);
                    }

                    break;
                case (Shape.TextMap, IReadOnlyDictionary<string, string> map):
                    foreach (var (key, text) in map)
                    {
                        yield return new Field($"{path}.{key}", text);
                    }

                    break;
                case (Shape.Json, JsonElement element):
                    foreach (Field field in FindInJson(element, path))
                    {
                        yield return field;
                    }

                    break;
                case (Shape.Record, _):
                    foreach (Field field in FindIn(value!, path + "."))
                    {
                        yield return field;
                    }

                    break;
                case (Shape.RecordList, System.Collections.IList items):
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (items[i] is { } item)
                        {
                            foreach (Field field in FindIn(item, $"{path}[{i}]."))
                            {
                                yield return field;
                            }
                        }
                    }

                    break;
            }
        }
    }

    private static IEnumerable<Field> FindInJson(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                yield return new Field(path, element.GetString()!);
                break;
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    foreach (Field field in FindInJson(property.Value, $"{path}.{property.Name}"))
                    {
                        yield return field;
                    }
                }

                break;
            case JsonValueKind.Array:
                int index = 0;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    foreach (Field field in FindInJson(item, $"{path}[{index++}]"))
                    {
                        yield return field;
                    }
                }

                break;
        }
    }

    private static async ValueTask<object> FillObjectAsync(object source, string prefix, FillText text, FillJson json)
    {
        object copy = Clone(source);
        foreach (Member member in PlanFor(source.GetType()))
        {
            object? value = member.Property.GetValue(source);
            string path = prefix + member.Name;
            object? filled = (member.Shape, value) switch
            {
                (_, null) => null,
                (Shape.Text, string s) => await text(path, s, member.Text),
                (Shape.TextList, IReadOnlyList<string> list) => await FillListAsync(list, path, member.Text, text),
                (Shape.TextMap, IReadOnlyDictionary<string, string> map) => await FillMapAsync(map, path, member.Text, text),
                (Shape.Json, JsonElement element) => await json(path, element),
                (Shape.Record, _) => await FillObjectAsync(value!, path + ".", text, json),
                (Shape.RecordList, System.Collections.IList items) => await FillRecordsAsync(items, member.Property.PropertyType, path, text, json),
                _ => value
            };

            if (filled is not null)
            {
                member.Property.SetValue(copy, filled);
            }
        }

        return copy;
    }

    private static async ValueTask<string[]> FillListAsync(IReadOnlyList<string> list, string path, TemplateText kind, FillText text)
    {
        var filled = new string[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            filled[i] = await text($"{path}[{i}]", list[i], kind);
        }

        return filled;
    }

    private static async ValueTask<Dictionary<string, string>> FillMapAsync(IReadOnlyDictionary<string, string> map, string path, TemplateText kind, FillText text)
    {
        var filled = new Dictionary<string, string>(map.Count);
        foreach (var (key, value) in map)
        {
            filled[key] = await text($"{path}.{key}", value, kind);
        }

        return filled;
    }

    private static async ValueTask<Array> FillRecordsAsync(System.Collections.IList items, Type listType, string path, FillText text, FillJson json)
    {
        Array filled = Array.CreateInstance(ElementType(listType)!, items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            filled.SetValue(items[i] is { } item ? await FillObjectAsync(item, $"{path}[{i}].", text, json) : null, i);
        }

        return filled;
    }

    // Every config is a record, and a record's compiler-made <Clone>$ copies it, init-only properties
    // and all; the copy's marked properties are then overwritten.
    private static object Clone(object source)
    {
        MethodInfo clone = source.GetType().GetMethod("<Clone>$")
            ?? throw new InvalidOperationException($"{source.GetType().Name} has templated fields but is not a record.");
        return clone.Invoke(source, null)!;
    }

    private static Member[] PlanFor(Type type) => Plans.GetOrAdd(type, static t =>
        t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => (Property: p, Attribute: p.GetCustomAttribute<TemplatedAttribute>()))
            .Where(m => m.Attribute is not null)
            .Select(m => new Member(
                m.Property,
                m.Property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(m.Property.Name),
                ShapeOf(m.Property),
                m.Attribute!.Text))
            .ToArray());

    private static Shape ShapeOf(PropertyInfo property)
    {
        Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (type == typeof(string))
        {
            return Shape.Text;
        }

        if (type == typeof(JsonElement))
        {
            return Shape.Json;
        }

        if (type == typeof(IReadOnlyList<string>))
        {
            return Shape.TextList;
        }

        if (type == typeof(IReadOnlyDictionary<string, string>))
        {
            return Shape.TextMap;
        }

        if (ElementType(type) is { IsClass: true })
        {
            return Shape.RecordList;
        }

        if (type.IsClass)
        {
            return Shape.Record;
        }

        throw new InvalidOperationException(
            $"{property.DeclaringType?.Name}.{property.Name} is marked [Templated] but a {type.Name} cannot be templated.");
    }

    private static Type? ElementType(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>) ? type.GetGenericArguments()[0] : null;
}
