using Cronos;

namespace Wbskt.Workflow.Abstraction.Runtime;

/// <summary>
/// Parses a cron expression accepting either the standard 5-field format (minute-level, no seconds)
/// or the 6-field format with a leading seconds field. Publish-time validation and the runtime ticker
/// must agree on this so a standard 5-field cron (e.g. "0 6 * * *") keeps firing after its first tick
/// instead of throwing on the second parse and being deleted as a one-time schedule.
/// </summary>
public static class CronParser
{
    public static bool TryParse(string expression, out CronExpression? cron)
    {
        try
        {
            cron = CronExpression.Parse(expression);
            return true;
        }
        catch (CronFormatException)
        {
            try
            {
                cron = CronExpression.Parse(expression, CronFormat.IncludeSeconds);
                return true;
            }
            catch (CronFormatException)
            {
                cron = null;
                return false;
            }
        }
    }

    public static bool TryGetNextOccurrence(string expression, DateTime afterUtc, out DateTime next)
    {
        if (!TryParse(expression, out CronExpression? cron))
        {
            next = default;
            return false;
        }

        DateTime? occurrence = cron!.GetNextOccurrence(afterUtc, TimeZoneInfo.Utc);
        if (!occurrence.HasValue)
        {
            next = default;
            return false;
        }

        next = occurrence.Value;
        return true;
    }
}
