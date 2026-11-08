-- Creates or updates a workflow's draft and returns it. @ExpectedRevision is the revision the editor
-- last read: NULL to start a draft, which must not exist yet. A draft that was saved, discarded or
-- published since is refused (50031), so a second editor never overwrites the first unseen.
--
-- A new draft records the workflow's current version as its base. A draft of a workflow that has never
-- been published counts against @MaxDrafts (NULL is no ceiling); drafts never count against the
-- workflow ceiling, since they register no triggers.
CREATE PROCEDURE dbo.WorkflowDraft_Save
    @RefId            UNIQUEIDENTIFIER,
    @WorkspaceId      INT,
    @Name             NVARCHAR(200),
    @Description      NVARCHAR(2000),
    @DefinitionJson   NVARCHAR(MAX),
    @UserId           INT,
    @UserRefId        UNIQUEIDENTIFIER = NULL,
    @ExpectedRevision INT = NULL,
    @MaxDrafts        INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRAN;

    DECLARE @DraftWorkspaceId INT, @Revision INT;

    SELECT @DraftWorkspaceId = WorkspaceId, @Revision = Revision
      FROM dbo.WorkflowDrafts WITH (UPDLOCK, HOLDLOCK)
     WHERE RefId = @RefId;

    IF @DraftWorkspaceId <> @WorkspaceId
       OR EXISTS (SELECT 1 FROM dbo.WorkflowDefinitions WHERE RefId = @RefId AND WorkspaceId <> @WorkspaceId)
    BEGIN
        ROLLBACK TRAN;
        THROW 50021, 'The workflow belongs to another workspace.', 1;
    END

    IF EXISTS (SELECT 1 FROM dbo.WorkflowDeletions WHERE RefId = @RefId)
    BEGIN
        ROLLBACK TRAN;
        THROW 50022, 'The workflow was deleted.', 1;
    END

    IF (@Revision IS NULL AND @ExpectedRevision IS NOT NULL)
       OR (@Revision IS NOT NULL AND (@ExpectedRevision IS NULL OR @ExpectedRevision <> @Revision))
    BEGIN
        ROLLBACK TRAN;
        THROW 50031, 'The draft was changed since it was read.', 1;
    END

    IF @Revision IS NOT NULL
    BEGIN
        UPDATE dbo.WorkflowDrafts
           SET Revision       = Revision + 1,
               Name           = @Name,
               Description    = @Description,
               DefinitionJson = @DefinitionJson,
               UpdatedBy      = @UserId,
               UpdatedByRefId = @UserRefId,
               UpdatedAt      = SYSUTCDATETIME()
         WHERE RefId = @RefId;
    END
    ELSE
    BEGIN
        DECLARE @BaseVersion INT = (SELECT MAX(Version) FROM dbo.WorkflowDefinitions WHERE RefId = @RefId AND Kind = N'Version');

        -- Counted under an application lock per workspace, so two new drafts cannot both take the last place.
        IF @BaseVersion IS NULL AND @MaxDrafts IS NOT NULL
        BEGIN
            DECLARE @Resource NVARCHAR(255) = CONCAT(N'workflow-draft-ceiling:', @WorkspaceId);
            EXEC sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction';

            IF (SELECT COUNT(*)
                  FROM dbo.WorkflowDrafts D
                 WHERE D.WorkspaceId = @WorkspaceId
                   AND NOT EXISTS (SELECT 1 FROM dbo.WorkflowDefinitions WD WHERE WD.RefId = D.RefId AND WD.Kind = N'Version')) >= @MaxDrafts
            BEGIN
                ROLLBACK TRAN;
                THROW 50032, 'The workspace has reached its limit of new workflow drafts.', 1;
            END
        END

        INSERT INTO dbo.WorkflowDrafts (RefId, WorkspaceId, BaseVersion, Revision, Name, Description, DefinitionJson, CreatedBy, UpdatedBy, UpdatedByRefId)
        VALUES (@RefId, @WorkspaceId, @BaseVersion, 1, @Name, @Description, @DefinitionJson, @UserId, @UserId, @UserRefId);
    END

    COMMIT TRAN;

    EXEC dbo.WorkflowDraft_GetBy_RefId @RefId = @RefId, @WorkspaceId = @WorkspaceId;
END;
GO
