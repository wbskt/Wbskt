using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models;

namespace Wbskt.Workflow.Engine.Host.Tests.Abstraction;

public class EdgeTests
{
    [Fact]
    public void Edge_roundtrips_json_two_element_arrays()
    {
        var nodeA = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var nodeB = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var original = new Edge((nodeA, "out"), (nodeB, "in"));
        var json = JsonSerializer.Serialize(original);

        Assert.Contains($"\"from\":[\"{nodeA}\",\"out\"]", json);
        Assert.Contains($"\"to\":[\"{nodeB}\",\"in\"]", json);

        var deserialized = JsonSerializer.Deserialize<Edge>(json);
        Assert.Equal(original, deserialized);
    }
}
