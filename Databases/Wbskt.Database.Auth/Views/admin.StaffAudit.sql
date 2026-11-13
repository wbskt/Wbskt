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
    SA.CreatedAt
FROM dbo.StaffAudit SA;
GO
