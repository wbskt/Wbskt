-- Replaces a client's tags with @Tags (an empty set clears them).
--
-- Scoped by workspace, so a client from another workspace changes nothing.
-- Returns the number of clients updated: 0 or 1.
CREATE PROCEDURE dbo.Client_SetTags
    @Id INT,
    @WorkspaceId INT,
    @Tags dbo.TagTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    IF NOT EXISTS (SELECT 1 FROM dbo.Clients WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id AND WorkspaceId = @WorkspaceId)
    BEGIN
        COMMIT TRANSACTION;
        SELECT 0 AS UpdatedCount;
        RETURN;
    END

    DELETE t
    FROM dbo.ClientTags t
    WHERE t.ClientId = @Id
      AND NOT EXISTS (SELECT 1 FROM @Tags n WHERE n.Tag = t.Tag);

    INSERT INTO dbo.ClientTags (ClientId, Tag)
    SELECT @Id, n.Tag
    FROM @Tags n
    WHERE NOT EXISTS (SELECT 1 FROM dbo.ClientTags t WHERE t.ClientId = @Id AND t.Tag = n.Tag);

    COMMIT TRANSACTION;
    SELECT 1 AS UpdatedCount;
END
GO
