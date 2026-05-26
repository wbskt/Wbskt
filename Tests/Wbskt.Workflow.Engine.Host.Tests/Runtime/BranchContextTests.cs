using System.Text.Json;
using System.Text.Json.Nodes;
using Wbskt.Workflow.Runtime;
using Xunit;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class BranchContextTests
{
    [Fact]
    public void GetVariable_ReturnsValue_WhenKeyExists()
    {
        // Arrange
        var ctx = new BranchContext
        {
            BranchRefId = Guid.NewGuid(),
            NodeId = Guid.NewGuid(),
            Local = new JsonObject
            {
                ["myKey"] = JsonValue.Create("myValue")
            }
        };

        // Act
        var result = ctx.GetVariable("myKey");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("myValue", result.GetValue<string>());
    }

    [Fact]
    public void GetVariable_ReturnsNull_WhenKeyDoesNotExist()
    {
        // Arrange
        var ctx = new BranchContext
        {
            BranchRefId = Guid.NewGuid(),
            NodeId = Guid.NewGuid(),
            Local = new JsonObject()
        };

        // Act
        var result = ctx.GetVariable("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void SetVariable_AddsVariable_WhenKeyDoesNotExist()
    {
        // Arrange
        var ctx = new BranchContext
        {
            BranchRefId = Guid.NewGuid(),
            NodeId = Guid.NewGuid(),
            Local = new JsonObject()
        };

        // Act
        ctx.SetVariable("newKey", JsonValue.Create(42));

        // Assert
        var result = ctx.GetVariable("newKey");
        Assert.NotNull(result);
        Assert.Equal(42, result.GetValue<int>());
    }

    [Fact]
    public void SetVariable_UpdatesVariable_WhenKeyExists()
    {
        // Arrange
        var ctx = new BranchContext
        {
            BranchRefId = Guid.NewGuid(),
            NodeId = Guid.NewGuid(),
            Local = new JsonObject
            {
                ["existingKey"] = JsonValue.Create("oldValue")
            }
        };

        // Act
        ctx.SetVariable("existingKey", JsonValue.Create("newValue"));

        // Assert
        var result = ctx.GetVariable("existingKey");
        Assert.NotNull(result);
        Assert.Equal("newValue", result.GetValue<string>());
    }

    [Fact]
    public void SetVariable_HandlesNull_Value()
    {
        // Arrange
        var ctx = new BranchContext
        {
            BranchRefId = Guid.NewGuid(),
            NodeId = Guid.NewGuid(),
            Local = new JsonObject()
        };

        // Act
        ctx.SetVariable("nullKey", null);

        // Assert
        var result = ctx.GetVariable("nullKey");
        Assert.Null(result);
    }

    [Fact]
    public void ClearVariable_RemovesVariable_WhenKeyExists()
    {
        // Arrange
        var ctx = new BranchContext
        {
            BranchRefId = Guid.NewGuid(),
            NodeId = Guid.NewGuid(),
            Local = new JsonObject
            {
                ["keyToRemove"] = JsonValue.Create("value")
            }
        };

        // Act
        ctx.ClearVariable("keyToRemove");

        // Assert
        var result = ctx.GetVariable("keyToRemove");
        Assert.Null(result);
    }

    [Fact]
    public void ClearVariable_DoesNotThrow_WhenKeyDoesNotExist()
    {
        // Arrange
        var ctx = new BranchContext
        {
            BranchRefId = Guid.NewGuid(),
            NodeId = Guid.NewGuid(),
            Local = new JsonObject()
        };

        // Act & Assert (no exception)
        ctx.ClearVariable("nonexistent");
    }

    [Fact]
    public void DeepClone_CreatesIndependentCopy()
    {
        // Arrange
        var original = new BranchContext
        {
            BranchRefId = Guid.NewGuid(),
            NodeId = Guid.NewGuid(),
            ParentBranchRefId = Guid.NewGuid(),
            ForkCohortId = Guid.NewGuid(),
            Local = new JsonObject
            {
                ["sharedKey"] = JsonValue.Create("originalValue")
            },
            LocalBag = new Dictionary<string, object?>
            {
                ["bagKey"] = "bagValue"
            },
            LastOutput = JsonDocument.Parse("{\"test\":123}").RootElement
        };

        // Act
        var clone = original.DeepClone();

        // Assert - values copied
        Assert.Equal(original.BranchRefId, clone.BranchRefId);
        Assert.Equal(original.NodeId, clone.NodeId);
        Assert.Equal(original.ParentBranchRefId, clone.ParentBranchRefId);
        Assert.Equal(original.ForkCohortId, clone.ForkCohortId);

        // Assert - JSON deep copied (modifying clone doesn't affect original)
        clone.SetVariable("sharedKey", JsonValue.Create("clonedValue"));
        Assert.Equal("originalValue", original.GetVariable("sharedKey")?.GetValue<string>());
        Assert.Equal("clonedValue", clone.GetVariable("sharedKey")?.GetValue<string>());

        // Assert - LocalBag is new dictionary (not shared reference)
        clone.LocalBag["bagKey"] = "modifiedValue";
        Assert.Equal("bagValue", original.LocalBag["bagKey"]);
        Assert.Equal("modifiedValue", clone.LocalBag["bagKey"]);

        // Assert - LastOutput is copied
        Assert.NotNull(clone.LastOutput);
        Assert.Equal(123, clone.LastOutput.Value.GetProperty("test").GetInt32());
    }

    [Fact]
    public void DeepClone_HandlesNullableFields()
    {
        // Arrange
        var original = new BranchContext
        {
            BranchRefId = Guid.NewGuid(),
            NodeId = Guid.NewGuid(),
            Local = new JsonObject()
        };

        // Act
        var clone = original.DeepClone();

        // Assert
        Assert.Null(clone.ParentBranchRefId);
        Assert.Null(clone.ForkCohortId);
        Assert.Null(clone.LastOutput);
        Assert.NotNull(clone.LocalBag);
        Assert.Empty(clone.LocalBag);
    }

    [Fact]
    public void LocalBag_IsIndependent_FromJsonLocal()
    {
        // Arrange
        var ctx = new BranchContext
        {
            BranchRefId = Guid.NewGuid(),
            NodeId = Guid.NewGuid(),
            Local = new JsonObject
            {
                ["jsonKey"] = JsonValue.Create("jsonValue")
            },
            LocalBag = new Dictionary<string, object?>
            {
                ["bagKey"] = "bagValue"
            }
        };

        // Assert - separate storage
        Assert.NotNull(ctx.GetVariable("jsonKey"));
        Assert.True(ctx.LocalBag.ContainsKey("bagKey"));
        Assert.False(ctx.LocalBag.ContainsKey("jsonKey"));
    }
}
