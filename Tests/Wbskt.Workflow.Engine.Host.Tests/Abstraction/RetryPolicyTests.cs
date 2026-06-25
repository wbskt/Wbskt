using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Workflow.Engine.Host.Tests.Abstraction;

public class RetryPolicyTests
{
    [Fact]
    public void RetryPolicy_deserialises_from_json()
    {
        var json = """
            {
                "strategy": "Exponential",
                "initialDelay": "00:00:01",
                "factor": 2.0,
                "maxDelay": "00:00:30",
                "maxAttempts": 5,
                "jitterPct": 10,
                "retryOn": ["Timeout", "ServiceUnavailable"]
            }
            """;

        var policy = JsonSerializer.Deserialize<RetryPolicy>(json);

        Assert.NotNull(policy);
        Assert.Equal(RetryStrategy.Exponential, policy!.Strategy);
        Assert.Equal(TimeSpan.FromSeconds(1), policy.InitialDelay);
        Assert.Equal(2.0, policy.Factor);
        Assert.Equal(TimeSpan.FromSeconds(30), policy.MaxDelay);
        Assert.Equal(5, policy.MaxAttempts);
        Assert.Equal(10, policy.JitterPct);
        Assert.Equal(2, policy.RetryOn.Count);
    }
}
