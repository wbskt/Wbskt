using System.Text.Json;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Validation;

// Several projects in the solution have a Program, so this one is named explicitly.
using ExampleExporter = Wbskt.Workflow.Exporter.Program;

namespace Wbskt.Workflow.Engine.Host.Tests.Abstraction;

/// <summary>
/// Guards the workflows that ship as examples.
/// </summary>
/// <remarks>
/// Anyone starting from an example inherits whatever is wrong with it, and the last defect here was
/// exactly that: a Logic gate whose condition was a bare string that could never evaluate, exported and
/// shipped for anyone to copy. These run against the exporter's own example builders rather than
/// a copy, because a copy cannot drift with the original.
/// </remarks>
public sealed class ExportedExampleSmokeTests
{
    private static readonly WorkflowValidator Validator = new();

    public static TheoryData<string> ExampleNames()
    {
        var data = new TheoryData<string>();
        foreach ((string name, _) in ExampleExporter.BuildExamples())
        {
            data.Add(name);
        }

        return data;
    }

    [Fact]
    public void The_exporter_ships_examples()
    {
        // Otherwise every test below passes vacuously.
        Assert.NotEmpty(ExampleExporter.BuildExamples());
    }

    [Theory]
    [MemberData(nameof(ExampleNames))]
    public void Every_exported_example_publishes_cleanly(string name)
    {
        WorkflowDefinition definition = Find(name);

        ValidationResult result = Validator.Validate(definition);

        Assert.True(
            result.IsValid,
            $"{name} would be rejected at publish: {string.Join("; ", result.Issues.Where(i => i.Severity == ValidationSeverity.Error).Select(i => $"[{i.Code}] {i.Message}"))}");
    }

    [Theory]
    [MemberData(nameof(ExampleNames))]
    public void Every_exported_example_still_validates_after_a_round_trip(string name)
    {
        // What ships is the JSON, not the builder output. A converter that loses a node's config - or a
        // condition that silently degrades on read - would leave the in-memory definition valid and the
        // shipped file broken, which is precisely the shape of the defect this item was opened for.
        WorkflowDefinition definition = Find(name);

        string json = JsonSerializer.Serialize(definition, ExampleExporter.SerializerOptions);
        WorkflowDefinition? roundTripped = JsonSerializer.Deserialize<WorkflowDefinition>(json, ExampleExporter.SerializerOptions);

        Assert.NotNull(roundTripped);
        Assert.Equal(definition.Nodes.Count, roundTripped!.Nodes.Count);
        Assert.Equal(definition.Edges.Count, roundTripped.Edges.Count);

        ValidationResult result = Validator.Validate(roundTripped);
        Assert.True(
            result.IsValid,
            $"{name} does not survive the round trip: {string.Join("; ", result.Issues.Where(i => i.Severity == ValidationSeverity.Error).Select(i => $"[{i.Code}] {i.Message}"))}");
    }

    private static WorkflowDefinition Find(string name)
    {
        return ExampleExporter.BuildExamples().Single(example => example.Name == name).Definition;
    }
}
