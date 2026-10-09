using Microsoft.AspNetCore.Mvc;
using Wbskt.EventBus.Abstractions;
using Wbskt.Management.Host.Controllers;
using Wbskt.Primitives;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

/// <summary>
/// "Controllers" in Docs/Coding.Conventions.md: a controller binds, calls one service and maps the
/// result. Publishing events and reaching the database belong to services, where they can be tested
/// and reused, and where the choice between the real and the queued bus is made once.
/// </summary>
public sealed class ControllerLayeringTests
{
    public static TheoryData<Type> Controllers()
    {
        var data = new TheoryData<Type>();
        foreach (var type in typeof(ClientsController).Assembly.GetTypes()
                     .Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t)))
        {
            data.Add(type);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Controllers))]
    public void A_controller_injects_no_event_bus_provider_or_reference_mapper(Type controller)
    {
        var offending = controller.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .Where(t => t == typeof(IEventBus) || t == typeof(IReferenceMapper) || t.Name.EndsWith("Provider", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offending);
    }
}
