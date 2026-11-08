using Microsoft.Extensions.Options;
using Wbskt.Infrastructure.Billing;
using Wbskt.Models.Billing;

namespace Wbskt.Host.Tests.Billing;

public sealed class PlanCatalogTests
{
    [Fact]
    public void Every_plan_has_built_in_limits()
    {
        Assert.Equal(Enum.GetValues<PlanCode>().Order(), PlanCatalog.BuiltIn.Keys.Order());
    }

    [Fact]
    public void A_paid_plan_never_allows_less_than_the_one_below_it()
    {
        PlanLimits[] ordered = [.. Enum.GetValues<PlanCode>().Select(code => PlanCatalog.BuiltIn[code])];

        for (int i = 1; i < ordered.Length; i++)
        {
            PlanLimits lower = ordered[i - 1];
            PlanLimits higher = ordered[i];
            Assert.True(higher.Clients >= lower.Clients);
            Assert.True(higher.MessagesPerMonth >= lower.MessagesPerMonth);
            Assert.True(higher.CreditsPerMonth >= lower.CreditsPerMonth);
            Assert.True(higher.ActiveRuns >= lower.ActiveRuns);
            Assert.True(higher.Workspaces >= lower.Workspaces);
            Assert.True(higher.Members >= lower.Members);
            Assert.True(higher.RawReadingsDays >= lower.RawReadingsDays);
            Assert.True(higher.HourlyReadingsDays >= lower.HourlyReadingsDays);
            Assert.True(higher.RunHistoryDays >= lower.RunHistoryDays);
        }
    }

    [Fact]
    public void Configuration_replaces_only_the_limits_it_sets()
    {
        PlanCatalog catalog = Catalog(new() { [PlanCode.Free] = new PlanLimitOverrides { Clients = 25 } });

        PlanLimits free = catalog.LimitsFor(PlanCode.Free);

        Assert.Equal(25, free.Clients);
        Assert.Equal(PlanCatalog.BuiltIn[PlanCode.Free] with { Clients = 25 }, free);
        Assert.Equal(PlanCatalog.BuiltIn[PlanCode.Pro], catalog.LimitsFor(PlanCode.Pro));
    }

    [Fact]
    public void A_tenant_override_wins_over_configuration()
    {
        PlanCatalog catalog = Catalog(new() { [PlanCode.Business] = new PlanLimitOverrides { Clients = 1_500 } });

        PlanLimits limits = catalog.LimitsFor(PlanCode.Business, new PlanLimitOverrides { Clients = 4_000, CreditsPerMonth = 1_000_000m });

        Assert.Equal(4_000, limits.Clients);
        Assert.Equal(1_000_000m, limits.CreditsPerMonth);
        Assert.Equal(PlanCatalog.BuiltIn[PlanCode.Business].Members, limits.Members);
    }

    [Fact]
    public void A_negative_limit_in_configuration_stops_the_host()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            Catalog(new() { [PlanCode.Maker] = new PlanLimitOverrides { ActiveRuns = -1 } }));

        Assert.Contains("Maker:ActiveRuns", exception.Message);
    }

    [Fact]
    public void Overrides_round_trip_through_storage_and_none_is_stored_as_null()
    {
        var overrides = new PlanLimitOverrides { Clients = 3_000, MessagesPerMonth = 250_000_000 };

        Assert.Equal(overrides, PlanLimitOverridesJson.Read(PlanLimitOverridesJson.Write(overrides)));
        Assert.Null(PlanLimitOverridesJson.Write(null));
        Assert.Null(PlanLimitOverridesJson.Write(new PlanLimitOverrides()));
        Assert.Null(PlanLimitOverridesJson.Read(null));
    }

    private static PlanCatalog Catalog(Dictionary<PlanCode, PlanLimitOverrides> plans) =>
        new(Options.Create(new PlanCatalogOptions { Plans = plans }));
}
