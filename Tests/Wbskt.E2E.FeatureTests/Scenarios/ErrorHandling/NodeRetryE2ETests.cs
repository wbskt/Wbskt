using System.Net;
using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.E2E.FeatureTests.Scenarios.ErrorHandling;

[Collection(E2ECollection.Name)]
public sealed class NodeRetryE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task Webhook_TransientFailures_BacksOffAndSucceeds()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        // 1. Setup local HTTP listener to simulate flaky endpoint
        // Use a host that Docker/localhost can resolve depending on environment.
        // For local mac/linux E2E, the host 'host.docker.internal' might be needed if engine is in docker,
        // but E2E tests run against localhost when run via IDE/CLI in the host OS.
        int port = GetAvailablePort();
        
        // Use the IP address directly to avoid localhost resolution issues across process boundaries
        string ipAddress = "127.0.0.1";
        string url = $"http://{ipAddress}:{port}/webhook/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(url);
        listener.Start();

        int requestCount = 0;
        var tcs = new TaskCompletionSource();
        
        var listenerTask = Task.Run(async () =>
        {
            try
            {
                while (listener.IsListening)
                {
                    var context = await listener.GetContextAsync();
                    requestCount++;
                    
                    if (requestCount <= 2)
                    {
                        // Fail the first 2 requests
                        context.Response.StatusCode = 503;
                        context.Response.Close();
                    }
                    else
                    {
                        // Succeed on the 3rd request
                        context.Response.StatusCode = 200;
                        context.Response.Close();
                        tcs.TrySetResult();
                        break;
                    }
                }
            }
            catch (Exception) when (!listener.IsListening)
            {
                // Expected when stopping
            }
        });

        // 2. Auth and setup
        var (token, workspaceRef) = await fixture.LoginAsAdminAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, "DeviceRetryTest");
        
        var storage = new InMemoryClientStorage(clientRefId, secret);
        var clientConfig = new ClientConfig(
            BaseApiUrl: E2EConfig.ManagementBaseUrl,
            BaseSocketUrl: E2EConfig.SocketWsBaseUrl,
            DeviceName: "DeviceRetryTest",
            PolicyPin: null);

        var workflowRefId = Guid.NewGuid();
        var deviceRefStr = clientRefId.ToString();
        var builder = new WorkflowBuilder($"E2E-Retry-{workflowRefId:N}", workflowRefId)
            .AddClientTrigger(deviceRefStr, "telemetry", WorkflowConcurrencyPolicy.AllowParallel, out var triggerNodeId)
            .AddWebhook("POST", url, null, webhook => 
            {
                webhook.OnSuccess(b => b.AddClientMessage(deviceRefStr, "WebhookSucceeded", "Webhook Succeeded", null));
                webhook.OnError(b => b.AddFailRun("Webhook failed permanently"));
            });

        var definition = builder.BuildAndValidate();
        var publishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, definition.Name,
            JsonSerializer.SerializeToElement(definition, JsonOpts));

        // 3. Connect client and listen for command
        var commands = new List<string>();
        var commandLock = new object();
        await using var wbsktClient = new WbsktClient(clientConfig, storage);
        wbsktClient.OnMessageReceived += (action, _) =>
        {
            lock (commandLock)
            {
                commands.Add(action);
            }
        };
        await wbsktClient.StartAsync();

        // 4. Trigger workflow
        await wbsktClient.SendAsync("telemetry", new { });

        var runRefId = await fixture.WaitForFirstRunAsync(token, workspaceRef, publishedRef, TimeSpan.FromSeconds(30));
        runRefId.Should().NotBe(Guid.Empty);

        // 5. Wait for the webhook to eventually succeed and trigger the command
        var arrived = await ServicesFixture.PollAsync(
            () =>
            {
                lock (commandLock)
                {
                    return Task.FromResult(commands.Contains("WebhookSucceeded"));
                }
            },
            timeout: TimeSpan.FromSeconds(30), // Allow time for retries
            interval: TimeSpan.FromSeconds(1));
            
        arrived.Should().BeTrue("client must receive WebhookSucceeded after retries");

        var summary = await fixture.WaitForRunTerminalAsync(token, workspaceRef, runRefId, TimeSpan.FromSeconds(15));
        summary.Should().NotBeNull();
        summary!.Status.Should().Be("Succeeded");

        requestCount.Should().Be(3, "webhook should have been called exactly 3 times (2 failures, 1 success)");

        listener.Stop();
        await listenerTask;
    }

    private static int GetAvailablePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
