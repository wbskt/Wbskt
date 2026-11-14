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
}
