-- The second stage of deleting a workspace (#965). dbo.Workspace_Retire shuts everything down at once
-- but deletes little, because runs and readings were still to go; this removes what is left once they
-- have: its clients and what they reported about themselves, its policies and message templates, then
-- its workflows and their definitions. Nobody can read them any more (the workspace is gone from the
-- auth database), and the privacy page promises that deleting removes everything. The event log is not
-- touched here: its rows, payloads included, go at the audit retention (dbo.EventLogs_DeleteBefore).
--
-- Acts only on a workspace whose account copy says it was retired, and in stages, each only once
-- nothing still needs the rows:
--   - a client once its readings are cleared (dbo.ClientReadings_Compact), up to @BatchSize a call,
--     its children in dbo.Client_Delete's order;
--   - the policies and templates once no client is left;
--   - the workflows and definitions once no run of them is left (dbo.Run_DeleteRetired, at its next sweep
--     once the workspace is retired), with their shared variables, run numbers and the workspace's credit cap.
-- The caller repeats it while it returns more than 0. Returns how many rows it deleted.
CREATE PROCEDURE dbo.Workspace_Purge
    @WorkspaceId INT,
    @BatchSize   INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.WorkspaceAccounts WHERE WorkspaceId = @WorkspaceId AND RetiredAt IS NOT NULL)
    BEGIN
        SELECT 0;
        RETURN;
    END

    DECLARE @Deleted INT = 0;
    DECLARE @Clients TABLE (Id INT PRIMARY KEY, RefId UNIQUEIDENTIFIER NOT NULL);

    BEGIN TRANSACTION;

    -- Revoked (2) or rejected (3) only: Workspace_Retire revoked every one that could connect.
    INSERT INTO @Clients (Id, RefId)
    SELECT TOP (@BatchSize) C.Id, C.RefId
    FROM dbo.Clients C WITH (UPDLOCK, ROWLOCK)
    WHERE C.WorkspaceId = @WorkspaceId
      AND C.Status IN (2, 3)
      AND NOT EXISTS (SELECT 1 FROM dbo.ClientReadings R WHERE R.ClientId = C.Id)
      AND NOT EXISTS (SELECT 1 FROM dbo.ClientReadingsHourly H WHERE H.ClientId = C.Id)
    ORDER BY C.Id;

    DELETE S FROM dbo.ClientStateVariables S INNER JOIN @Clients X ON X.Id = S.ClientId;
    DELETE M FROM dbo.ClientMessageTypes M INNER JOIN @Clients X ON X.Id = M.ClientId;
    DELETE A FROM dbo.ClientCapabilities A INNER JOIN @Clients X ON X.Id = A.ClientId;
    DELETE T FROM dbo.ClientTags T INNER JOIN @Clients X ON X.Id = T.ClientId;
    DELETE Q FROM dbo.QueuedCommands Q INNER JOIN @Clients X ON X.Id = Q.ClientId;
    DELETE U FROM dbo.ClientMessageUsageHourly U INNER JOIN @Clients X ON X.Id = U.ClientId;
    -- A fired hold keeps the client's last matching message; its key starts with the client's RefId
    -- (ClientHoldTriggerKey), so the prefix seeks UQ_ClientHoldStates_TriggerKey.
    DELETE H FROM dbo.ClientHoldStates H INNER JOIN @Clients X ON H.TriggerKey LIKE N'hold:' + LOWER(CONVERT(NVARCHAR(36), X.RefId)) + N':%';
    DELETE C FROM dbo.Clients C INNER JOIN @Clients X ON X.Id = C.Id;
    SET @Deleted += @@ROWCOUNT;

    IF NOT EXISTS (SELECT 1 FROM dbo.Clients WHERE WorkspaceId = @WorkspaceId)
    BEGIN
        DELETE FROM dbo.MessageTemplates WHERE WorkspaceId = @WorkspaceId;
        SET @Deleted += @@ROWCOUNT;

        DELETE FROM dbo.RegistrationPolicies WHERE WorkspaceId = @WorkspaceId;
        SET @Deleted += @@ROWCOUNT;
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.Runs R WHERE R.WorkspaceId = @WorkspaceId)
    BEGIN
        DECLARE @Workflows TABLE (RefId UNIQUEIDENTIFIER PRIMARY KEY);
        INSERT INTO @Workflows (RefId)
        SELECT RefId FROM dbo.Workflows WHERE WorkspaceId = @WorkspaceId
        UNION
        SELECT RefId FROM dbo.WorkflowDefinitions WHERE WorkspaceId = @WorkspaceId;

        DELETE V FROM dbo.SharedVariables V INNER JOIN @Workflows W ON W.RefId = V.WorkflowRefId;
        DELETE S FROM dbo.WorkflowRunSequences S INNER JOIN @Workflows W ON W.RefId = S.WorkflowRefId;

        -- Retire deleted these already; a registration written after it would block the definitions.
        DELETE TR FROM dbo.TriggerRegistrations TR INNER JOIN dbo.WorkflowDefinitions D ON D.Id = TR.WorkflowDefinitionId WHERE D.WorkspaceId = @WorkspaceId;
        DELETE SF FROM dbo.ScheduledFires SF INNER JOIN dbo.WorkflowDefinitions D ON D.Id = SF.WorkflowDefinitionId WHERE D.WorkspaceId = @WorkspaceId;

        -- Retire already cleared LiveDefinitionId, the one reference from a workflow to a definition.
        DELETE FROM dbo.Workflows WHERE WorkspaceId = @WorkspaceId;
        SET @Deleted += @@ROWCOUNT;

        DELETE FROM dbo.WorkflowDefinitions WHERE WorkspaceId = @WorkspaceId;
        SET @Deleted += @@ROWCOUNT;

        DELETE FROM dbo.WorkspaceCreditCaps WHERE WorkspaceId = @WorkspaceId;
    END

    COMMIT TRANSACTION;

    SELECT @Deleted;
END
GO
