-- Up to @Take staff audit rows written before @Before, for StaffAuditRetention. @Before is seven years
-- ago at most: TR_StaffAudit_AppendOnly refuses the whole batch if any row in it is younger.
CREATE PROCEDURE dbo.StaffAudit_DeleteExpired
    @Before DATETIME2(3),
    @Take   INT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE TOP (@Take)
    FROM dbo.StaffAudit
    WHERE CreatedAt < @Before;

    SELECT @@ROWCOUNT AS Deleted;
END
