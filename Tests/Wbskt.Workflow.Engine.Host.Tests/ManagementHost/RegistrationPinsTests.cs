using FluentAssertions;
using Wbskt.Management.Host.Services;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

public sealed class RegistrationPinsTests
{
    [Fact]
    public void Generated_pins_fit_the_column()
    {
        // RegistrationPolicies.Pin is NVARCHAR(20).
        RegistrationPins.Generate().Length.Should().Be(RegistrationPins.Length).And.BeLessThanOrEqualTo(20);
    }

    [Fact]
    public void Generated_pins_avoid_lookalike_characters()
    {
        string pins = string.Concat(Enumerable.Range(0, 500).Select(_ => RegistrationPins.Generate()));

        pins.Should().MatchRegex("^[23456789ABCDEFGHJKMNPQRSTVWXYZ]+$");
    }

    [Fact]
    public void Generated_pins_are_unique()
    {
        string[] pins = Enumerable.Range(0, 1000).Select(_ => RegistrationPins.Generate()).ToArray();

        pins.Distinct().Should().HaveCount(pins.Length);
    }
}
