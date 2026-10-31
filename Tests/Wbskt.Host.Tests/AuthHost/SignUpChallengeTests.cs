using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Host.Telemetry;
using Wbskt.Auth.Models;

namespace Wbskt.Host.Tests.AuthHost;

/// <summary>
/// The proof of work registration asks for (#152). These run the in-process spent-challenge store;
/// the Redis store is one SET NX and is exercised end to end.
/// </summary>
public sealed class SignUpChallengeTests
{
    private static SignUpChallenges Challenges(SignUpChallengeOptions? options = null, TimeProvider? time = null) =>
        new(Options.Create(options ?? new SignUpChallengeOptions { Difficulty = 8 }), null, time ?? TimeProvider.System, new AuthMetrics(), NullLogger<SignUpChallenges>.Instance);

    [Fact]
    public async Task A_solved_challenge_is_accepted_once()
    {
        var challenges = Challenges();
        var issued = challenges.Issue();
        var solution = SignUpProofOfWork.Solve(issued.Challenge, issued.Difficulty);

        Assert.True((await challenges.RedeemAsync(issued.Challenge, solution)).IsSuccess);

        var replay = await challenges.RedeemAsync(issued.Challenge, solution);
        Assert.Equal("SIGNUP_CHALLENGE_INVALID", replay.Error.Code);
    }

    [Fact]
    public async Task A_missing_challenge_or_solution_is_required()
    {
        var challenges = Challenges();
        var issued = challenges.Issue();

        Assert.Equal("SIGNUP_CHALLENGE_REQUIRED", (await challenges.RedeemAsync(null, null)).Error.Code);
        Assert.Equal("SIGNUP_CHALLENGE_REQUIRED", (await challenges.RedeemAsync(issued.Challenge, "")).Error.Code);
    }

    [Fact]
    public async Task A_wrong_solution_is_refused_and_does_not_spend_the_challenge()
    {
        var challenges = Challenges(new SignUpChallengeOptions { Difficulty = 16 });
        var issued = challenges.Issue();
        var solution = SignUpProofOfWork.Solve(issued.Challenge, issued.Difficulty);
        var wrong = Enumerable.Range(0, 1000).Select(i => $"x{i}").First(s => !SignUpProofOfWork.Satisfies(issued.Challenge, s, issued.Difficulty));

        Assert.Equal("SIGNUP_CHALLENGE_INVALID", (await challenges.RedeemAsync(issued.Challenge, wrong)).Error.Code);
        Assert.True((await challenges.RedeemAsync(issued.Challenge, solution)).IsSuccess);
    }

    [Fact]
    public async Task An_expired_challenge_is_refused()
    {
        var clock = new ManualClock();
        var challenges = Challenges(new SignUpChallengeOptions { Difficulty = 4, LifetimeSeconds = 600 }, clock);
        var issued = challenges.Issue();
        var solution = SignUpProofOfWork.Solve(issued.Challenge, issued.Difficulty);

        clock.Advance(TimeSpan.FromSeconds(601));

        Assert.Equal("SIGNUP_CHALLENGE_INVALID", (await challenges.RedeemAsync(issued.Challenge, solution)).Error.Code);
    }

    [Fact]
    public async Task A_challenge_from_another_key_or_with_a_lowered_difficulty_is_refused()
    {
        var challenges = Challenges(new SignUpChallengeOptions { Difficulty = 12 });
        var foreign = Challenges(new SignUpChallengeOptions { Difficulty = 12 }).Issue();
        var foreignSolution = SignUpProofOfWork.Solve(foreign.Challenge, foreign.Difficulty);

        Assert.Equal("SIGNUP_CHALLENGE_INVALID", (await challenges.RedeemAsync(foreign.Challenge, foreignSolution)).Error.Code);

        // The difficulty is signed, so a caller cannot rewrite it to 0 and send any solution.
        var issued = challenges.Issue();
        var lowered = issued.Challenge.Replace("v1.12.", "v1.0.");
        Assert.Equal("SIGNUP_CHALLENGE_INVALID", (await challenges.RedeemAsync(lowered, "0")).Error.Code);
    }

    [Fact]
    public async Task A_shared_key_lets_another_replica_redeem()
    {
        var key = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());
        var issuing = Challenges(new SignUpChallengeOptions { Difficulty = 8, Key = key });
        var redeeming = Challenges(new SignUpChallengeOptions { Difficulty = 8, Key = key });
        var issued = issuing.Issue();

        var result = await redeeming.RedeemAsync(issued.Challenge, SignUpProofOfWork.Solve(issued.Challenge, issued.Difficulty));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void A_short_key_is_refused_at_construction()
    {
        Assert.Throws<InvalidOperationException>(() => Challenges(new SignUpChallengeOptions { Key = Convert.ToBase64String(new byte[8]) }));
    }

    [Fact]
    public async Task Turned_off_it_accepts_registrations_without_a_challenge()
    {
        var challenges = Challenges(new SignUpChallengeOptions { Enabled = false });

        Assert.True((await challenges.RedeemAsync(null, null)).IsSuccess);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(14)]
    public void Solve_finds_a_solution_that_satisfies_the_difficulty(int difficulty)
    {
        var solution = SignUpProofOfWork.Solve("v1.test", difficulty);

        Assert.True(SignUpProofOfWork.Satisfies("v1.test", solution, difficulty));
    }

    /// <summary>
    /// Pins the hash definition the console's browser solver also implements: SHA-256 of the UTF-8
    /// "challenge:solution", zero bits counted from the top of the first byte. "abc:0" hashes to
    /// 0x5f36..., which has exactly one.
    /// </summary>
    [Fact]
    public void Leading_zero_bits_are_counted_from_the_top_of_the_first_byte()
    {
        Assert.True(SignUpProofOfWork.Satisfies("abc", "0", 1));
        Assert.False(SignUpProofOfWork.Satisfies("abc", "0", 2));
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
