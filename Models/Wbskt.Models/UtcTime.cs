namespace Wbskt.Models;

public static class UtcTime
{
    /// <summary>
    /// A stored time as a UTC <see cref="DateTimeOffset"/>. A <c>DATETIME2</c> read from SQL has
    /// <see cref="DateTimeKind.Unspecified"/> but holds UTC; the implicit conversion would take it as local time.
    /// </summary>
    public static DateTimeOffset ToUtcOffset(this DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => new DateTimeOffset(value),
        DateTimeKind.Local => new DateTimeOffset(value.ToUniversalTime()),
        _ => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
    };

    public static DateTimeOffset? ToUtcOffset(this DateTime? value) => value?.ToUtcOffset();
}
