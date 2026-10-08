-- Removes a client, its tags and what it reported about itself. Its event-log rows stay: they keep the
-- client's RefId, and FK_EventLogs_Client was dropped so history does not pin the row (and so a
-- message logged after the delete cannot fail the whole buffered insert batch).
--
-- Scoped by workspace in the WHERE clause, so a client from another workspace deletes nothing.
-- Returns the number of clients deleted: 0 or 1.
CREATE PROCEDURE dbo.Client_Delete
    @Id INT,
    @WorkspaceId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    IF NOT EXISTS (SELECT 1 FROM dbo.Clients WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id AND WorkspaceId = @WorkspaceId)
    BEGIN
        COMMIT TRANSACTION;
        SELECT 0 AS DeletedCount;
        RETURN;
    END

    DELETE FROM dbo.ClientStateVariables WHERE ClientId = @Id;
    DELETE FROM dbo.ClientCapabilities WHERE ClientId = @Id;
    DELETE FROM dbo.ClientTags WHERE ClientId = @Id;
    DELETE FROM dbo.Clients WHERE Id = @Id AND WorkspaceId = @WorkspaceId;

    COMMIT TRANSACTION;
    SELECT 1 AS DeletedCount;
END
GO
