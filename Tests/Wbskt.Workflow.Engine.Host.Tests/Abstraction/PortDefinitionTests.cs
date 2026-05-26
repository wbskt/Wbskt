using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Xunit;

namespace Wbskt.Workflow.Engine.Host.Tests.Abstraction;

public class PortDefinitionTests
{
    [Fact]
    public void Port_serialises_to_json_with_id_direction_and_label()
    {
        var port = new PortDefinition("true", PortDirection.Output, "True branch");

        var json = System.Text.Json.JsonSerializer.Serialize(port);

        Assert.Contains("\"portId\":\"true\"", json);
        Assert.Contains("\"direction\":\"Output\"", json);
        Assert.Contains("\"label\":\"True branch\"", json);
    }
}
