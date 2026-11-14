using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Host.Services.Email;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;
using Wbskt.Infrastructure.StaffActions;
using Wbskt.Models.Admin;

namespace Wbskt.Host.Tests.AuthHost;

public sealed class AdminUserServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly StaffChange Staff = new(Guid.NewGuid(), "pika", "Asked by the owner on a call", Guid.NewGuid());
    private static readonly AdminUserActionRequest Request = new(Staff);

    private readonly Mock<IAccountProvider> _accounts = new();
    private readonly Mock<IStaffUserProvider> _staffUsers = new();
    private readonly Mock<ISessionProvider> _sessions = new();
    private readonly Mock<IAccessTokenRevocation> _accessTokens = new();
    private readonly Mock<IAuthMailer> _mailer = new();
    private readonly RecordingRunner _runner = new();
    private readonly User _user = new() { Id = 42, RefId = Guid.NewGuid(), Username = "alice", Email = "alice@example.test", IsActive = true };

    public AdminUserServiceTests()
    {
        _accounts.Setup(a => a.FindIdByRefIdAsync(_user.RefId, It.IsAny<CancellationToken>())).ReturnsAsync(_user.Id);
        _accounts.Setup(a => a.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_user);
    }

    private AdminUserService Service() => new(_accounts.Object, _staffUsers.Object, _sessions.Object, _accessTokens.Object, _mailer.Object,
        _runner, new FixedClock(Now), NullLogger<AdminUserService>.Instance);

    [Fact]
    public async Task An_unknown_person_is_not_found_and_nothing_runs()
    {
        var result = await Service().SendPasswordResetAsync(Guid.NewGuid(), Request, CancellationToken.None);

        Assert.Equal("USER_NOT_FOUND", result.Error.Code);
        Assert.Null(_runner.Action);
    }

    [Fact]
    public async Task A_lockout_is_cleared_as_a_staff_action_on_the_person()
    {
        _user.LockedUntil = Now.UtcDateTime.AddMinutes(10);

        var result = await Service().ClearLockoutAsync(_user.RefId, Request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new StaffAction(AdminUserService.ClearLockoutAction, AdminUserService.TargetKind, _user.RefId), _runner.Action);
        _staffUsers.Verify(s => s.ClearLockoutAsync(42, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task A_person_whose_lock_has_lapsed_is_not_locked()
    {
        _user.LockedUntil = Now.UtcDateTime.AddMinutes(-1);

        var result = await Service().ClearLockoutAsync(_user.RefId, Request, CancellationToken.None);

        Assert.Equal("USER_NOT_LOCKED", result.Error.Code);
        Assert.Null(_runner.Action);
    }

    [Fact]
    public async Task A_password_reset_is_mailed_only_after_the_change_commits()
    {
        _runner.Inspect = () => _mailer.VerifyNoOtherCalls();

        var result = await Service().SendPasswordResetAsync(_user.RefId, Request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _accounts.Verify(a => a.CreatePasswordResetTokenAsync(42, It.IsAny<byte[]>(), Now.UtcDateTime.Add(PasswordService.PasswordResetLifetime), AdminUserService.StaffAddress, It.IsAny<CancellationToken>()));
        _mailer.Verify(m => m.QueuePasswordResetAsync("alice@example.test", "alice", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_replayed_request_mails_nothing()
    {
        _runner.Replay = true;

        var result = await Service().SendPasswordResetAsync(_user.RefId, Request, CancellationToken.None);

        Assert.True(result.Value.Replayed);
        _mailer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task No_mail_is_sent_for_a_person_whose_sign_in_is_off()
    {
        _user.IsActive = false;

        var reset = await Service().SendPasswordResetAsync(_user.RefId, Request, CancellationToken.None);
        var verify = await Service().ResendVerificationAsync(_user.RefId, Request, CancellationToken.None);

        Assert.Equal("USER_SIGN_IN_OFF", reset.Error.Code);
        Assert.Equal("USER_SIGN_IN_OFF", verify.Error.Code);
        _mailer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_verified_address_is_not_sent_another_link()
    {
        _user.IsEmailVerified = true;

        var result = await Service().ResendVerificationAsync(_user.RefId, Request, CancellationToken.None);

        Assert.Equal("EMAIL_ALREADY_VERIFIED", result.Error.Code);
        _mailer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_verification_link_is_mailed_after_its_token_is_stored()
    {
        var result = await Service().ResendVerificationAsync(_user.RefId, Request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _accounts.Verify(a => a.CreateEmailVerificationTokenAsync(42, It.IsAny<byte[]>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()));
        _mailer.Verify(m => m.QueueEmailVerificationAsync("alice@example.test", "alice", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Signing_out_everywhere_revokes_refresh_then_access_tokens()
    {
        _runner.Inspect = () => _accessTokens.VerifyNoOtherCalls();

        var result = await Service().SignOutEverywhereAsync(_user.RefId, Request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _sessions.Verify(s => s.RevokeAllRefreshTokensForUserAsync(42, AdminUserService.StaffAddress, It.IsAny<CancellationToken>()));
        _accessTokens.Verify(t => t.RevokeUserAsync(_user.RefId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Turning_sign_in_off_ends_the_access_tokens_too()
    {
        var result = await Service().SetSignInAsync(_user.RefId, new AdminSetUserSignInRequest(Staff, false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AdminUserService.BlockSignInAction, _runner.Action!.Name);
        _staffUsers.Verify(s => s.SetActiveAsync(42, false, AdminUserService.StaffAddress, It.IsAny<CancellationToken>()));
        _accessTokens.Verify(t => t.RevokeUserAsync(_user.RefId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Turning_sign_in_on_leaves_tokens_alone()
    {
        _user.IsActive = false;

        var result = await Service().SetSignInAsync(_user.RefId, new AdminSetUserSignInRequest(Staff, true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AdminUserService.AllowSignInAction, _runner.Action!.Name);
        _accessTokens.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Asking_for_the_sign_in_it_already_has_is_refused()
    {
        var result = await Service().SetSignInAsync(_user.RefId, new AdminSetUserSignInRequest(Staff, true), CancellationToken.None);

        Assert.Equal("USER_SIGN_IN_UNCHANGED", result.Error.Code);
        Assert.Null(_runner.Action);
    }

    [Fact]
    public async Task A_mail_that_fails_after_the_commit_still_reports_the_change()
    {
        _mailer.Setup(m => m.QueuePasswordResetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("queue closed"));

        var result = await Service().SendPasswordResetAsync(_user.RefId, Request, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingRunner : IStaffActionRunner
    {
        public StaffAction? Action { get; private set; }

        public bool Replay { get; set; }

        /// <summary>Runs after the change, while its transaction would still be open.</summary>
        public System.Action? Inspect { get; set; }

        public async Task<Result<StaffActionResponse>> RunAsync(StaffChange staff, StaffAction action, Func<CancellationToken, Task<Result>> change, CancellationToken cancellationToken)
        {
            Action = action;
            if (Replay)
            {
                return new StaffActionResponse(1, true);
            }

            var outcome = await change(cancellationToken);
            Inspect?.Invoke();
            return outcome.IsFailure ? outcome.Error : new StaffActionResponse(1, false);
        }
    }
}
