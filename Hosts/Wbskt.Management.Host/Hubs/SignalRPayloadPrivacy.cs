using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events;
using Wbskt.Infrastructure.Json;

namespace Wbskt.Management.Host.Hubs;

/// <summary>
/// The JSON the hub sends events with: the shared settings, minus every property marked
/// <see cref="SignalRPrivateAttribute"/> on the event or on an interface it implements. The internal ids
/// and the actor's address stay on the bus; a browser only ever sees the references.
/// </summary>
internal static class SignalRPayloadPrivacy
{
    public static void Apply(JsonSerializerOptions options)
    {
        options.TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { HidePrivateProperties }
        };
        WbsktJsonExtensions.Apply(options);
    }

    internal static void HidePrivateProperties(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object || !typeof(IEvent).IsAssignableFrom(typeInfo.Type))
        {
            return;
        }

        var interfaces = typeInfo.Type.GetInterfaces();
        foreach (var property in typeInfo.Properties)
        {
            if (IsPrivate(property, interfaces))
            {
                property.ShouldSerialize = (_, _) => false;
            }
        }
    }

    private static bool IsPrivate(JsonPropertyInfo property, Type[] interfaces)
    {
        if (property.AttributeProvider?.IsDefined(typeof(SignalRPrivateAttribute), false) == true)
        {
            return true;
        }

        // By the CLR name: JsonPropertyInfo.Name is the camel-cased JSON name, which no interface declares.
        if (property.AttributeProvider is not MemberInfo member)
        {
            return false;
        }

        return interfaces
            .Select(i => i.GetProperty(member.Name))
            .Any(p => p is not null && p.IsDefined(typeof(SignalRPrivateAttribute), false));
    }
}
