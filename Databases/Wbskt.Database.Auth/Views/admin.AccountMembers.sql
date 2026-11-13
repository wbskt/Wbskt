CREATE VIEW [admin].AccountMembers
AS
SELECT
    TM.TenantId,
    TM.UserId,
    TM.IsSuspended
FROM dbo.TenantMembers TM;
GO
