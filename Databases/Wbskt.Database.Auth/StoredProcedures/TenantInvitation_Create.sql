-- Issues an invitation. Supersedes any invitation still outstanding for the same address in the
-- same tenant: re-inviting someone is the normal way to reissue a token that was lost or has
-- expired, and it must not fail on UX_TenantInvitations_Pending.
CREATE PROCEDURE dbo.TenantInvitation_Create
    @TenantId INT,
    @Email NVARCHAR(100),
    @RoleId INT,
    @TokenHash VARBINARY(32),
    @ExpiresAt DATETIME2(3),
    @InvitedByUserId INT,
    @RefId UNIQUEIDENTIFIER OUTPUT,
    -- The invitation mail names the tenant the invitee is being asked to join. Returned from here
    -- rather than fetched separately: the caller already has the tenant id and this costs nothing.
    @TenantName NVARCHAR(100) OUTPUT,
    -- Comma-separated Workspaces.Id values the invitee joins on acceptance. NULL or empty for none.
    -- Every one must belong to @TenantId.
    @WorkspaceIds NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @RoleId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Id = @RoleId AND TenantId = @TenantId)
    BEGIN
        THROW 50001, 'Role does not belong to the specified tenant.', 1;
    END

    IF EXISTS (
        SELECT 1
        FROM dbo.TenantMembers TM
        INNER JOIN dbo.Users U ON U.Id = TM.UserId
        WHERE TM.TenantId = @TenantId AND U.Email = @Email)
    BEGIN
        THROW 50012, 'That user is already a member of this tenant.', 1;
    END

    DECLARE @Workspaces TABLE (WorkspaceId INT NOT NULL PRIMARY KEY);
    INSERT INTO @Workspaces (WorkspaceId)
    SELECT DISTINCT CAST(value AS INT)
    FROM STRING_SPLIT(ISNULL(@WorkspaceIds, N''), N',')
    WHERE LTRIM(RTRIM(value)) <> N'';

    IF EXISTS (
        SELECT 1
        FROM @Workspaces WS
        LEFT JOIN dbo.Workspaces W ON W.Id = WS.WorkspaceId AND W.TenantId = @TenantId
        WHERE W.Id IS NULL)
    BEGIN
        THROW 50017, 'Workspace does not belong to the specified tenant.', 1;
    END

    SELECT @TenantName = Name FROM dbo.Tenants WHERE Id = @TenantId;

    SET @RefId = NEWID();

    BEGIN TRANSACTION;

    UPDATE dbo.TenantInvitations
    SET RevokedAt = SYSUTCDATETIME()
    WHERE TenantId = @TenantId
      AND Email = @Email
      AND AcceptedAt IS NULL
      AND RevokedAt IS NULL;

    INSERT INTO dbo.TenantInvitations (RefId, TenantId, Email, RoleId, TokenHash, ExpiresAt, InvitedByUserId)
    VALUES (@RefId, @TenantId, @Email, @RoleId, @TokenHash, @ExpiresAt, @InvitedByUserId);

    INSERT INTO dbo.TenantInvitationWorkspaces (InvitationId, WorkspaceId)
    SELECT SCOPE_IDENTITY(), WorkspaceId
    FROM @Workspaces;

    COMMIT TRANSACTION;
END
GO
