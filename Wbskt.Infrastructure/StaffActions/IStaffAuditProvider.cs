namespace Wbskt.Infrastructure.StaffActions;

/// <summary>The host database's <c>dbo.StaffAudit</c>, which only ever grows.</summary>
public interface IStaffAuditProvider
{
    Task<StaffAuditRow?> FindByIdempotencyKeyAsync(Guid idempotencyKey, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the row and returns its id. Throws a <c>SqlException</c> numbered 2627 when another row
    /// already holds the key.
    /// </summary>
    Task<long> InsertAsync(StaffAuditEntry entry, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes up to <paramref name="take"/> rows written before <paramref name="beforeUtc"/>, which must be
    /// older than <see cref="StaffAuditRetention.Years"/> years: the table refuses any other delete. Returns how many went.
    /// </summary>
    Task<int> DeleteExpiredAsync(DateTime beforeUtc, int take, CancellationToken cancellationToken);
}
