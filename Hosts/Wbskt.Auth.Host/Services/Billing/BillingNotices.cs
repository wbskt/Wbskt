using System.Globalization;
using Wbskt.Auth.Host.Models;
using Wbskt.Models.Admin;
using Wbskt.Models.Billing;

namespace Wbskt.Auth.Host.Services.Billing;

/// <summary>
/// The words of each billing notice: what happened, with the number and the date, and what to do next.
/// Each is keyed so it is sent once: a month's warnings by the month, a payment's by the payment, a
/// plan change by its revision.
/// </summary>
internal static class BillingNotices
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>
    /// <paramref name="reachedIn"/> is the month the allowance was reached in, or default for the month of
    /// <paramref name="at"/>. Reaching it in a month already over (posted just before it ended and announced
    /// after) is told in the past tense, keyed to that month (#876).
    /// </summary>
    public static BillingNotice CreditAllowance(PlanCode plan, int percent, decimal allowance, decimal used, DateOnly reachedIn, DateTime at)
    {
        DateTime month = MonthOf(reachedIn, at);
        if (percent >= 100 && month < StartOfMonth(at))
        {
            return LateCreditAllowance(plan, allowance, used, month);
        }

        string key = Month(month);
        string resets = Date(NextMonth(month));
        if (plan == PlanCode.Business)
        {
            return BusinessCreditAllowance(percent, allowance, used, key, resets);
        }

        return percent >= 100
            ? new BillingNotice(BillingNoticeKind.CreditAllowance100, key,
                "Your account has used this month's workflow credits",
                "This month's workflow credits are used",
                [
                    $"Your account's workspaces have used {Number(used)} workflow credits this month, all {Number(allowance)} your plan includes.",
                    $"New runs now draw on the credit packs you have bought. With none left, new runs are refused until {resets}, when the allowance starts again, and runs already going stop at their next step.",
                    "To keep runs going, buy a credit pack or move to a larger plan."
                ])
            : new BillingNotice(BillingNoticeKind.CreditAllowance80, key,
                $"Your account has used {percent}% of this month's workflow credits",
                $"{percent}% of this month's workflow credits are used",
                [
                    $"Your account's workspaces have used {Number(used)} of the {Number(allowance)} workflow credits your plan includes this month.",
                    $"Past the allowance, runs draw on the credit packs you have bought; with none left, new runs are refused until {resets} and runs already going stop at their next step.",
                    "If you expect to need more, buy a credit pack or move to a larger plan."
                ]);
    }

    // Business runs carry on past the allowance and the overage is invoiced, so its notices say what it will cost.
    private static BillingNotice BusinessCreditAllowance(int percent, decimal allowance, decimal used, string month, string resets)
    {
        string ceiling = Number(allowance * BusinessContract.CreditOverageMultiple);
        return percent >= 100
            ? new BillingNotice(BillingNoticeKind.CreditAllowance100, month,
                "Your account has used this month's workflow credits",
                "This month's workflow credits are used",
                [
                    $"Your account's workspaces have used {Number(used)} workflow credits this month, all {Number(allowance)} your contract includes.",
                    $"Runs carry on. Credits past the allowance come from your credit packs first, then are invoiced at {OverageRate()} under your contract, up to {ceiling} credits in all this month. Past that, new runs are refused until {resets} and runs already going stop at their next step.",
                    "If you expect to need more every month, reply to resize your contract."
                ])
            : new BillingNotice(BillingNoticeKind.CreditAllowance80, month,
                $"Your account has used {percent}% of this month's workflow credits",
                $"{percent}% of this month's workflow credits are used",
                [
                    $"Your account's workspaces have used {Number(used)} of the {Number(allowance)} workflow credits your contract includes this month.",
                    $"Past the allowance runs carry on, invoiced at {OverageRate()}, up to {ceiling} credits in all this month; past that, new runs are refused until {resets} and runs already going stop at their next step.",
                    "If you expect to need more every month, reply to resize your contract."
                ]);
    }

    // Nothing is left to warn of, so it says what happened and what it meant, and that the month has started again.
    private static BillingNotice LateCreditAllowance(PlanCode plan, decimal allowance, decimal used, DateTime month)
    {
        string name = MonthName(month);
        string restarted = Date(NextMonth(month));
        string past = plan == PlanCode.Business
            ? $"Credits past it came from your credit packs first, then are invoiced for {name} at {OverageRate()} under your contract."
            : "Runs past it drew on the credit packs you have bought. Once none were left, new runs were refused and runs going stopped at their next step.";
        string next = plan == PlanCode.Business
            ? "If you expect to need more every month, reply to resize your contract."
            : "If you often need more, a larger plan includes more credits each month.";
        return new BillingNotice(BillingNoticeKind.CreditAllowance100, Month(month),
            $"Your account used all of its workflow credits in {name}",
            $"{name}'s workflow credits were used",
            [
                $"By the end of {name} your account's workspaces had used {Number(used)} workflow credits, all {Number(allowance)} your {(plan == PlanCode.Business ? "contract" : "plan")} includes.",
                past,
                $"The allowance started again on {restarted}. {next}"
            ]);
    }

    /// <summary>
    /// <paramref name="reachedIn"/> is the month the cap was reached in, or default for the month of
    /// <paramref name="at"/>; a month already over is told in the past tense (#876).
    /// </summary>
    public static BillingNotice CreditCap(int workspaceId, string workspaceName, int percent, decimal cap, decimal spent, DateOnly reachedIn, DateTime at)
    {
        DateTime month = MonthOf(reachedIn, at);
        string key = $"{Month(month)}:{workspaceId.ToString(Invariant)}";
        string resets = Date(NextMonth(month));
        if (percent >= 100 && month < StartOfMonth(at))
        {
            string name = MonthName(month);
            return new BillingNotice(BillingNoticeKind.CreditCap100, key,
                $"{workspaceName} reached its monthly credit cap in {name}",
                $"{workspaceName} reached its credit cap in {name}",
                [
                    $"The workspace {workspaceName} spent {Number(spent)} workflow credits in {name}, its cap of {Number(cap)}.",
                    $"Its new runs were refused for the rest of {name}, and have been allowed again since {resets}. If it needs more in a month, raise or remove the cap in the workspace's settings."
                ]);
        }

        return percent >= 100
            ? new BillingNotice(BillingNoticeKind.CreditCap100, key,
                $"{workspaceName} reached its monthly credit cap",
                $"{workspaceName} reached its credit cap",
                [
                    $"The workspace {workspaceName} has spent {Number(spent)} workflow credits this month, its cap of {Number(cap)}.",
                    $"Its new runs are refused, and its runs already going stop at their next step, until {resets} or until you raise or remove the cap in the workspace's settings."
                ])
            : new BillingNotice(BillingNoticeKind.CreditCap80, key,
                $"{workspaceName} has spent {percent}% of its monthly credit cap",
                $"{workspaceName} is near its credit cap",
                [
                    $"The workspace {workspaceName} has spent {Number(spent)} of the {Number(cap)} workflow credits its cap allows this month.",
                    $"At the cap its new runs are refused until {resets}, and its runs going stop at their next step. Raise or remove the cap in the workspace's settings to avoid that."
                ]);
    }

    public static BillingNotice MessagesApproaching(PlanCode plan, long allowance, long used, DateTime at) =>
        new(BillingNoticeKind.Messages80, MessagesKey(at, allowance),
            "Your account has carried 80% of this month's messages",
            "80% of this month's messages are used",
            [
                $"Your account's clients have carried {Number(used)} of the {Number(allowance)} messages your {plan} plan includes this month.",
                plan switch
                {
                    PlanCode.Free => $"At the allowance each client is held to one message a minute until {Date(NextMonth(at))}. Move to a paid plan to avoid that.",
                    PlanCode.Business => "Past the allowance your clients carry on; your contract covers what is carried beyond it.",
                    _ => "Past the allowance nothing more is charged and your clients carry on. Far past it they are slowed, so if traffic keeps growing, consider a larger plan."
                }
            ]);

    public static BillingNotice MessagesReached(PlanCode plan, long allowance, long used, bool throttled, DateTime at) =>
        new(BillingNoticeKind.Messages100, MessagesKey(at, allowance),
            "Your account has carried this month's messages",
            "This month's messages are used",
            [
                $"Your account's clients have carried {Number(used)} messages this month, past the {Number(allowance)} your {plan} plan includes.",
                (throttled, plan) switch
                {
                    (true, _) => $"Each client is now held to one message a minute until {Date(NextMonth(at))}. Move to a larger plan to lift that now.",
                    (false, PlanCode.Business) => "Your clients carry on; your contract covers what is carried beyond the allowance.",
                    _ => "Nothing more is charged and your clients carry on. Far past the allowance they are slowed, so if traffic keeps growing, consider a larger plan."
                }
            ]);

    public static BillingNotice MessagesThrottled(PlanCode plan, long allowance, long throttledAt, long used, DateTime at) =>
        new(BillingNoticeKind.MessagesThrottled, MessagesKey(at, allowance),
            "Your account's clients are now slowed",
            "Your clients are slowed until the 1st",
            [
                $"Your account's clients have carried {Number(used)} messages this month. Your {plan} plan includes {Number(allowance)}, and past {Number(throttledAt)} clients are slowed.",
                $"Each client is now held to one message a minute until {Date(NextMonth(at))}. Nothing extra is charged. Move to a larger plan to lift that now."
            ]);

    public static BillingNotice PaymentFailed(string subscriptionId, PlanCode plan, DateTime pastDueSince, TimeSpan grace) =>
        new(BillingNoticeKind.PaymentFailed, PastDueKey(subscriptionId, pastDueSince),
            $"A payment for your {plan} plan failed",
            "A payment failed",
            [
                $"The payment for your {plan} subscription failed on {Date(pastDueSince)}.",
                $"Your account keeps {plan} until {Date(pastDueSince + grace)}. If the payment has not gone through by then, the account moves to Free and its Free limits apply.",
                "Update your payment method from the billing page so the next attempt succeeds."
            ]);

    public static BillingNotice GraceEnding(string subscriptionId, PlanCode plan, DateTime pastDueSince, TimeSpan grace, int retentionGraceDays) =>
        new(BillingNoticeKind.GraceEnding, PastDueKey(subscriptionId, pastDueSince),
            $"Your account moves to Free on {Date(pastDueSince + grace)}",
            "Your subscription is still unpaid",
            [
                $"The payment for your {plan} subscription has been failing since {Date(pastDueSince)}.",
                $"On {Date(pastDueSince + grace)} the account moves to Free: new clients, workspaces and members past Free's limits are refused, and history older than Free keeps is removed {retentionGraceDays.ToString(Invariant)} days later.",
                "Update your payment method from the billing page to keep your plan."
            ]);

    /// <param name="removedFrom">When history the new plan no longer keeps starts being removed.</param>
    public static BillingNotice Downgraded(PlanCode previous, PlanLimits before, PlanCode current, PlanLimits after, int planRevision, DateTime removedFrom)
    {
        var paragraphs = new List<string> { $"Your account moved from {previous} to {current}. These limits are now lower:" };
        Lower(paragraphs, "Clients", before.Clients, after.Clients);
        Lower(paragraphs, "Messages a month", before.MessagesPerMonth, after.MessagesPerMonth);
        Lower(paragraphs, "Workflow credits a month", before.CreditsPerMonth, after.CreditsPerMonth);
        Lower(paragraphs, "Runs at once", before.ActiveRuns, after.ActiveRuns);
        Lower(paragraphs, "Workspaces", before.Workspaces, after.Workspaces);
        Lower(paragraphs, "Members", before.Members, after.Members);
        paragraphs.Add("Nothing already there is removed, but new clients, workspaces and members past these limits are refused.");

        var kept = new List<string>();
        Shorter(kept, "raw readings", before.RawReadingsDays, after.RawReadingsDays);
        Shorter(kept, "hourly readings", before.HourlyReadingsDays, after.HourlyReadingsDays);
        Shorter(kept, "run history", before.RunHistoryDays, after.RunHistoryDays);
        if (kept.Count > 0)
        {
            paragraphs.Add($"{current} keeps {string.Join(", ", kept)}. Older history is kept until {Date(removedFrom)} and removed after that, unless the account moves back to a plan that keeps it.");
        }

        return new BillingNotice(BillingNoticeKind.Downgraded, $"revision:{planRevision.ToString(Invariant)}",
            $"Your account is now on {current}",
            $"Your account moved to {current}",
            paragraphs);
    }

    /// <param name="remaining">What the grant still holds undrawn; a grant spent to nothing is never announced.</param>
    public static BillingNotice PackExpiring(string source, string sourceRef, decimal remaining, DateTime expiresAt) =>
        new(BillingNoticeKind.PackExpiring, $"{source}:{sourceRef}",
            $"{Number(remaining)} workflow credits expire on {Date(expiresAt)}",
            "Credits expire soon",
            [
                source == StaffCreditGrants.Source
                    ? $"{Number(remaining)} workflow credits Wbskt support added to your account are still unspent and expire on {Date(expiresAt)}."
                    : $"{Number(remaining)} workflow credits from a credit pack on your account are still unspent and expire on {Date(expiresAt)}.",
                "Until then, runs past your plan's monthly allowance draw on them. After that date they can no longer be spent."
            ]);

    private static void Lower(List<string> paragraphs, string label, decimal before, decimal after)
    {
        if (after < before)
        {
            paragraphs.Add($"{label}: {Number(before)} → {Number(after)}");
        }
    }

    private static void Shorter(List<string> kept, string label, int before, int after)
    {
        if (after < before)
        {
            kept.Add($"{label} for {after.ToString(Invariant)} days (was {before.ToString(Invariant)})");
        }
    }

    private static string PastDueKey(string subscriptionId, DateTime pastDueSince) =>
        $"{subscriptionId}:{pastDueSince.ToString("yyyy-MM-ddTHH:mm:ss.fff", Invariant)}";

    private static string Month(DateTime at) => at.ToString("yyyy-MM", Invariant);

    // The allowance is part of the period, so a plan change within the month is told again against the new one.
    private static string MessagesKey(DateTime at, long allowance) => $"{Month(at)}:{allowance.ToString(Invariant)}";

    private static string MonthName(DateTime month) => month.ToString("MMMM yyyy", Invariant);

    // The first of the month something happened in: the one it names, or else the month of at.
    private static DateTime MonthOf(DateOnly month, DateTime at) =>
        month == default ? StartOfMonth(at) : month.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    private static DateTime StartOfMonth(DateTime at) => new(at.Year, at.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    private static string OverageRate() =>
        $"${BusinessContract.UsdPerThousandExtraCredits.ToString("0.00", Invariant)} per 1,000 credits";

    private static DateTime NextMonth(DateTime at) => StartOfMonth(at).AddMonths(1);

    private static string Date(DateTime at) => at.ToString("d MMMM yyyy", Invariant);

    private static string Number(decimal value) => value.ToString("#,0.##", Invariant);

    private static string Number(long value) => value.ToString("N0", Invariant);
}
