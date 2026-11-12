using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events;
using Wbskt.Events.Client;
using Wbskt.Events.Management;
using Wbskt.Management.Host.Hubs;

namespace Wbskt.Host.Tests.ManagementHost;

/// <summary>
/// What a browser receives over the hub: an event without its internal ids, whether the event itself or
/// an interface it implements marks them <see cref="SignalRPrivateAttribute"/>.
/// </summary>
public sealed class SignalRPayloadPrivacyTests
{
    private static readonly JsonSerializerOptions HubOptions = CreateHubOptions();

    [Fact]
    public void A_client_event_goes_out_without_its_client_and_workspace_ids()
    {
        var json = Serialize(new ClientConnectedEvent(Guid.NewGuid(), 42, 7, "gateway-1"));

        Assert.False(json.ContainsKey("clientId"));
        Assert.False(json.ContainsKey("workspaceId"));
        Assert.True(json.ContainsKey("clientRefId"));
        Assert.True(json.ContainsKey("hostId"));
    }

    [Fact]
    public void A_policy_event_from_an_actor_goes_out_without_ids_or_the_actors_address()
    {
        var json = Serialize(new PolicyDeletedEvent(Guid.NewGuid(), 3, 7, "lab") { ActorUserId = 9, ClientAddress = "203.0.113.5", UserAgent = "curl" });

        Assert.False(json.ContainsKey("policyId"));
        Assert.False(json.ContainsKey("workspaceId"));
        Assert.False(json.ContainsKey("actorUserId"));
        Assert.False(json.ContainsKey("clientAddress"));
        Assert.False(json.ContainsKey("userAgent"));
        Assert.True(json.ContainsKey("policyRefId"));
    }

    [Fact]
    public void Every_private_property_of_every_event_is_left_out()
    {
        var events = typeof(ClientConnectedEvent).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IEvent).IsAssignableFrom(t));

        var leaked = new List<string>();
        foreach (var type in events)
        {
            var typeInfo = HubOptions.GetTypeInfo(type);
            foreach (var property in typeInfo.Properties)
            {
                if (property.AttributeProvider is MemberInfo member && IsMarkedPrivate(type, member.Name) && property.ShouldSerialize is null)
                {
                    leaked.Add($"{type.Name}.{member.Name}");
                }
            }
        }

        Assert.Empty(leaked);
    }

    private static bool IsMarkedPrivate(Type type, string name) =>
        new[] { type }.Concat(type.GetInterfaces())
            .Select(t => t.GetProperty(name))
            .Any(p => p is not null && p.IsDefined(typeof(SignalRPrivateAttribute), false));

    private static JsonObject Serialize(object @event) =>
        JsonSerializer.SerializeToNode(@event, @event.GetType(), HubOptions)!.AsObject();

    private static JsonSerializerOptions CreateHubOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        SignalRPayloadPrivacy.Apply(options);
        return options;
    }
}
