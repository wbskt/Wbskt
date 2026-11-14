-- What staff did, for the admin host. IdempotencyKey is a staff credit grant's reference, which the admin
-- matches to the grant in the main database to count the credits each staff member gave.
CREATE VIEW [admin].StaffAudit
AS
SELECT
    SA.Id,
    SA.StaffRefId,
    SA.StaffName,
    SA.Action,
    SA.TargetKind,
    SA.TargetRefId,
    SA.Reason,
    SA.Outcome,
    SA.Detail,
    SA.IdempotencyKey,
    SA.CreatedAt
FROM dbo.StaffAudit SA;
GO
