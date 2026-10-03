using System.Text.Json;
using Wbskt.Workflow.NodeExecutors.Controls;

namespace Wbskt.Workflow.Engine.Host.Tests.NodeExecutors.Controls;

public sealed class CanonicalJsonTests
{
    [Theory]
    [InlineData("1", "1")]
    [InlineData("1.0", "1")]
    [InlineData("1.50", "1.5")]
    [InlineData("1e2", "100")]
    [InlineData("-0.0", "0")]
    [InlineData("0.000", "0")]
    [InlineData("1e300", "1E+300")]
    public void Numbers_are_written_in_one_form(string input, string expected)
    {
        Assert.Equal(expected, CanonicalJson.Serialize(Parse(input)));
    }

    [Fact]
    public void Properties_are_sorted_at_every_depth_and_whitespace_is_dropped()
    {
        string json = CanonicalJson.Serialize(Parse("""{ "b": { "y": 1, "x": [ {"d": 2, "c": 1} ] }, "a": "text" }"""));

        Assert.Equal("""{"a":"text","b":{"x":[{"c":1,"d":2}],"y":1}}""", json);
    }

    [Fact]
    public void Array_order_is_kept()
    {
        Assert.Equal("[3,1,2]", CanonicalJson.Serialize(Parse("[3, 1.0, 2]")));
    }

    [Fact]
    public void Strings_booleans_and_null_are_unchanged()
    {
        JsonElement value = Parse("""["a\"b", "é", true, false, null]""");

        // The same escaping the default serializer uses, so stored strings keep their existing text.
        Assert.Equal(JsonSerializer.Serialize(value), CanonicalJson.Serialize(value));
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
