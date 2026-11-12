-- Stores the numbers from one state report. They share the report's client time, receive time and
-- lateness, so those come once rather than per row. A report from a client deleted meanwhile, from one
-- no longer registered (revoked or rejected), or from a client of a deleted workspace, is dropped, so
-- nothing lands after its history was queued for clearing (dbo.DeletedClientReadings). The status
-- check holds once a deleted workspace's copy is pruned (dbo.WorkspaceAccount_DeleteRetired), since
-- dbo.Workspace_Retire revokes every client it had.
CREATE PROCEDURE dbo.ClientReadings_InsertBatch
    @ClientId   INT,
    @SentAt     DATETIME2(3),
    @ReceivedAt DATETIME2(3),
    @IsLate     BIT,
    @Readings   dbo.ClientReadingTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    -- The retired check is a locking read, not the row version: a report racing the retire waits for
    -- it and is dropped, rather than landing after the queued clear has finished and dropped its row.
    INSERT INTO dbo.ClientReadings (ClientId, Name, SentAt, ReceivedAt, NumberValue, IsLate)
    SELECT @ClientId, Name, @SentAt, @ReceivedAt, NumberValue, @IsLate
    FROM @Readings
    WHERE EXISTS (
        SELECT 1
        FROM dbo.Clients c
        WHERE c.Id = @ClientId
          AND c.Status IN (0, 1)
          AND NOT EXISTS (SELECT 1 FROM dbo.WorkspaceAccounts a WITH (READCOMMITTEDLOCK) WHERE a.WorkspaceId = c.WorkspaceId AND a.RetiredAt IS NOT NULL));
END
GO
