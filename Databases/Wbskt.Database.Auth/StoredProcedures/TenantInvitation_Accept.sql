-- Redeems an invitation: joins the accepting user to the tenant, grants the invited role if one was
-- named, and adds them to the workspaces the invitation lists. This is the authoritative validity check -- callers may look an invitation up first for
-- a better error, but only this procedure may act on one.
--
-- The accepting account's email must match the address the invitation was sent to. Without that,
-- possession of a leaked link is enough to join a tenant, and an invitation stops being addressed
-- at anyone in particular.
--
-- @MemberLimit is the tenant's plan's limit on members (NULL is none). Joining past it raises 50031;
-- someone already a member joins nothing, so is never refused. The tenant's row is locked first,
-- before the invitation's, so concurrent acceptances one short of the limit take turns.
--
-- An unknown token, a spent one and a mismatched address all raise the same error. They are the
-- same answer to the caller -- distinguishing them would let a holder of one tenant's token probe
-- for which addresses another invitation was issued to.
CREATE PROCEDURE dbo.TenantInvitation_Accept
    @TokenHash VARBINARY(32),
    @UserId INT,
    @TenantId INT OUTPUT,
    @MemberLimit INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    DECLARE @InvitationId INT, @RoleId INT;

    -- Lock order: the tenant row, then the invitation row. Role_Delete and User_Delete hold the
    -- tenant row and then write invitations, so taking the invitation first deadlocked against them.
    -- The tenant is read from the invitation without a lock (an invitation never changes tenant),
    -- and the locked read below is what decides whether the invitation is still good.
    DECLARE @InvitedTenantId INT = (SELECT TenantId FROM dbo.TenantInvitations WHERE TokenHash = @TokenHash);

    SELECT @TenantId = Id FROM dbo.Tenants WITH (UPDLOCK, HOLDLOCK) WHERE Id = @InvitedTenantId;

    -- UPDLOCK/HOLDLOCK: two concurrent redemptions of the same token must not both pass the
    -- AcceptedAt IS NULL test. The row is held for the duration of the transaction, so the second
    -- one reads the accepted row and fails the guard below.
    SELECT @InvitationId = I.Id,
           @RoleId = I.RoleId
    FROM dbo.TenantInvitations I WITH (UPDLOCK, HOLDLOCK)
    INNER JOIN dbo.Users U ON U.Id = @UserId
    WHERE I.TokenHash = @TokenHash
      AND I.TenantId = @InvitedTenantId
      AND I.AcceptedAt IS NULL
      AND I.RevokedAt IS NULL
      AND I.ExpiresAt > SYSUTCDATETIME()
      AND I.Email = U.Email;

    IF @InvitationId IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50011, 'Invitation is not valid.', 1;
    END

    -- The tenant row is held, so concurrent acceptances one short of the limit take turns.
    IF @MemberLimit IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM dbo.TenantMembers WHERE TenantId = @TenantId AND UserId = @UserId)
       AND (SELECT COUNT(*) FROM dbo.TenantMembers WHERE TenantId = @TenantId) >= @MemberLimit
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50031, 'The plan has reached its member limit.', 1;
    END

    UPDATE dbo.TenantInvitations
    SET AcceptedAt = SYSUTCDATETIME(),
        AcceptedByUserId = @UserId
    WHERE Id = @InvitationId;

    IF NOT EXISTS (SELECT 1 FROM dbo.TenantMembers WHERE TenantId = @TenantId AND UserId = @UserId)
    BEGIN
        INSERT INTO dbo.TenantMembers (TenantId, UserId)
        VALUES (@TenantId, @UserId);

        -- An invitation that names no role joins a new member with the tenant's User role, so they can
        -- see the tenant at all; a member already in it keeps the roles they have.
        IF @RoleId IS NULL
        BEGIN
            SET @RoleId = (SELECT Id FROM dbo.Roles WHERE TenantId = @TenantId AND Kind = N'User');
        END
    END

    -- Tenant-wide (WorkspaceId NULL). The invited role is the tenant's answer to "what can a new
    -- member do"; narrowing it to a workspace is a later, explicit act by an administrator.
    IF @RoleId IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM dbo.UserRoles WHERE UserId = @UserId AND RoleId = @RoleId AND TenantId = @TenantId AND WorkspaceId IS NULL)
    BEGIN
        INSERT INTO dbo.UserRoles (UserId, RoleId, TenantId, WorkspaceId)
        VALUES (@UserId, @RoleId, @TenantId, NULL);
    END

    -- Only workspaces still in this tenant: one deleted since the invitation was issued has already
    -- cascaded out of the list, and the tenant check guards anything else.
    INSERT INTO dbo.WorkspaceMembers (WorkspaceId, UserId)
    SELECT IW.WorkspaceId, @UserId
    FROM dbo.TenantInvitationWorkspaces IW
    INNER JOIN dbo.Workspaces W ON W.Id = IW.WorkspaceId AND W.TenantId = @TenantId
    WHERE IW.InvitationId = @InvitationId
      AND NOT EXISTS (SELECT 1 FROM dbo.WorkspaceMembers WM WHERE WM.WorkspaceId = IW.WorkspaceId AND WM.UserId = @UserId);

    COMMIT TRANSACTION;
END
GO
