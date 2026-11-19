using System.Text.Json;
using System.Text.Json.Nodes;
using Wbskt.Host.Tests.Architecture;
using Wbskt.Infrastructure.Billing;
using Wbskt.Models.Billing;

namespace Wbskt.Host.Tests.Billing;

/// <summary>
/// Docs/Billing.Plans.json is the plan ladder as the code has it: each plan's built-in limits and its
/// self-serve prices, the credit packs, and the Business contract's rates. The website's plans table and the billing docs page are copied from it rather than
/// typed again, so this fails when the code moves and the file does not. Run with WBSKT_UPDATE_SNAPSHOTS=1
/// to rewrite it, and commit the file with the change.
/// </summary>
public sealed class PlanSnapshotTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    [Fact]
    public void The_plan_snapshot_matches_the_catalogue_and_the_prices()
    {
        string path = Path.Combine(RepoPaths.Root(), "Docs", "Billing.Plans.json");
        string expected = Snapshot();

        if (Environment.GetEnvironmentVariable("WBSKT_UPDATE_SNAPSHOTS") == "1")
        {
            File.WriteAllText(path, expected);
        }

        Assert.True(File.Exists(path), $"{path} is missing. Run the tests with WBSKT_UPDATE_SNAPSHOTS=1 to write it.");
        Assert.Equal(expected, File.ReadAllText(path).ReplaceLineEndings("\n"));
    }

    private static string Snapshot()
    {
        var plans = new JsonArray();
        foreach (PlanCode plan in Enum.GetValues<PlanCode>())
        {
            var prices = new JsonObject();
            foreach (BillingInterval interval in Enum.GetValues<BillingInterval>())
            {
                if (PlanPrices.UsdFor(plan, interval) is { } usd)
                {
                    prices[JsonNamingPolicy.CamelCase.ConvertName(interval.ToString())] = usd;
                }
            }

            plans.Add(new JsonObject
            {
                ["plan"] = plan.ToString(),
                ["limits"] = JsonSerializer.SerializeToNode(PlanCatalog.BuiltIn[plan], Json),
                ["pricesUsd"] = prices
            });
        }

        var packs = new JsonArray();
        foreach ((string name, CreditPack pack) in CreditPackPrices.Packs.OrderBy(p => p.Value.Credits))
        {
            packs.Add(new JsonObject
            {
                ["name"] = name,
                ["credits"] = pack.Credits,
                ["usd"] = pack.Usd
            });
        }

        var document = new JsonObject
        {
            ["plans"] = plans,
            ["creditPacks"] = packs,
            ["business"] = new JsonObject
            {
                ["minimumClients"] = BusinessContract.MinimumClients,
                ["usdPerMillionExtraMessages"] = BusinessContract.UsdPerMillionExtraMessages,
                ["usdPerThousandExtraCredits"] = BusinessContract.UsdPerThousandExtraCredits
            }
        };

        return document.ToJsonString(Json).ReplaceLineEndings("\n") + "\n";
    }
}
