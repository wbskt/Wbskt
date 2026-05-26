using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models;
using Xunit;

namespace Wbskt.Workflow.Engine.Host.Tests.Abstraction;

public class EdgeTests
{
    [Fact]
    public void Edge_roundtrips_json_two_element_arrays()
    {
        var original = new Edge(("A", "out"), ("B", "in"));
        var json = JsonSerializer.Serialize(original);

        Assert.Contains("\"from\":[\"A\",\"out\"]", json);
        Assert.Contains("\"to\":[\"B\",\"in\"]", json);

        var deserialized = JsonSerializer.Deserialize<Edge>(json);
        Assert.Equal(original, deserialized);
    }
}
