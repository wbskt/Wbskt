using System.Text.Json;
using Wbskt.Workflow.Builder;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Workflow.Exporter;

public class Program
{
    public static void Main(string[] args)
    {
        var outputDir = args.Length > 0 ? args[0] : "Examples";
        Directory.CreateDirectory(outputDir);

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        Console.WriteLine("Generating Workflow Examples...");

        var examples = new[]
        {
            ("LinearHappyPath", BuildHappyPath()),
            ("BranchingWithLogicAndForEach", BuildBranchingWorkflow()),
            ("ErrorHandlingAndCompensation", BuildErrorHandlingWorkflow())
        };

        foreach (var (name, def) in examples)
        {
            var json = JsonSerializer.Serialize(def, options);
            var path = Path.Combine(outputDir, $"{name}.json");
            File.WriteAllText(path, json);
            Console.WriteLine($"Exported: {path}");
        }
        
        Console.WriteLine("Done.");
    }

    private static Wbskt.Workflow.Abstraction.Models.WorkflowDefinition BuildHappyPath()
    {
        var builder = new WorkflowBuilder("Linear Happy Path", Guid.NewGuid());
        return builder
            .AddClientTrigger("sensor-A", "telemetry", WorkflowConcurrencyPolicy.AllowParallel, out _)
            .AddDelay(TimeSpan.FromMinutes(1))
            .AddClientMessage("client-A", "OpenVent")
            .BuildAndValidate();
    }

    private static Wbskt.Workflow.Abstraction.Models.WorkflowDefinition BuildBranchingWorkflow()
    {
        var builder = new WorkflowBuilder("Branching Workflow", Guid.NewGuid());
        builder.AddClientTrigger("sensor-A", "telemetry", WorkflowConcurrencyPolicy.AllowParallel, out _);

        builder.AddLogicGate("event.value > 100", logic => 
        {
            logic.OnTrue(b => 
            {
                b.AddClientMessage("client-B", "AlarmOn")
                 .AddParallelForEach("event.subItems", loop => 
                 {
                     loop.OnBody(lb => lb.AddClientMessage("client-C", "CheckItem"));
                 });
                 
                // Note: Linear flow after ParallelForEach automatically connects from "empty" port
                b.AddDelay(TimeSpan.FromSeconds(30));
            });
            
            logic.OnFalse(b => 
            {
                b.AddFailRun("Value too low");
            });
        });

        return builder.BuildAndValidate();
    }

    private static Wbskt.Workflow.Abstraction.Models.WorkflowDefinition BuildErrorHandlingWorkflow()
    {
        var builder = new WorkflowBuilder("Error Handling Workflow", Guid.NewGuid());
        builder.AddScheduleTrigger("0 0 * * *", out _)
               .AddWebhook("POST", "https://api.example.com/start", null, out var webhookId);

        // Webhook error handling
        builder.SetHead(webhookId, PortNames.Error)
               .AddFailRun("Webhook failed to start process");

        // Webhook success path
        builder.SetHead(webhookId, PortNames.Default)
               .AddClientMessage("client-A", "Initialize")
               .AddAwaitSignal("ProcessCompleted", TimeSpan.FromMinutes(60), out var waitId);

        // Signal Timeout path
        builder.SetHead(waitId, PortNames.Timeout)
               .AddFailRun("Process timed out");

        return builder.BuildAndValidate();
    }
}
