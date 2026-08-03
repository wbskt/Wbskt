-- Redeems an invitation: joins the accepting user to the tenant and grants the invited role, if one
-- was named. This is the authoritative validity check -- callers may look an invitation up first for
-- a better error, but only this procedure may act on one.
--
-- The accepting account's email must match the address the invitation was sent to. Without that,
-- possession of a leaked link is enough to join a tenant, and an invitation stops being addressed
-- at anyone in particular.
--
-- An unknown token, a spent one and a mismatched address all raise the same error. They are the
-- same answer to the caller -- distinguishing them would let a holder of one tenant's token probe
-- for which addresses another invitation was issued to.
CREATE PROCEDURE dbo.TenantInvitation_Accept
    @TokenHash VARBINARY(32),
    @UserId INT,
    @TenantId INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    DECLARE @InvitationId INT, @RoleId INT;

    -- UPDLOCK/HOLDLOCK: two concurrent redemptions of the same token must not both pass the
    -- AcceptedAt IS NULL test. The row is held for the duration of the transaction, so the second
    -- one reads the accepted row and fails the guard below.
    SELECT @InvitationId = I.Id,
           @TenantId = I.TenantId,
           @RoleId = I.RoleId
    FROM dbo.TenantInvitations I WITH (UPDLOCK, HOLDLOCK)
    INNER JOIN dbo.Users U ON U.Id = @UserId
    WHERE I.TokenHash = @TokenHash
      AND I.AcceptedAt IS NULL
      AND I.RevokedAt IS NULL
      AND I.ExpiresAt > SYSUTCDATETIME()
      AND I.Email = U.Email;

    IF @InvitationId IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50011, 'Invitation is not valid.', 1;
    END

    UPDATE dbo.TenantInvitations
    SET AcceptedAt = SYSUTCDATETIME(),
        AcceptedByUserId = @UserId
    WHERE Id = @InvitationId;

    IF NOT EXISTS (SELECT 1 FROM dbo.TenantMembers WHERE TenantId = @TenantId AND UserId = @UserId)
    BEGIN
        INSERT INTO dbo.TenantMembers (TenantId, UserId)
        VALUES (@TenantId, @UserId);
    END

    -- Tenant-wide (WorkspaceId NULL). The invited role is the tenant's answer to "what can a new
    -- member do"; narrowing it to a workspace is a later, explicit act by an administrator.
    IF @RoleId IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM dbo.UserRoles WHERE UserId = @UserId AND RoleId = @RoleId AND TenantId = @TenantId AND WorkspaceId IS NULL)
    BEGIN
        INSERT INTO dbo.UserRoles (UserId, RoleId, TenantId, WorkspaceId)
        VALUES (@UserId, @RoleId, @TenantId, NULL);
    END

    COMMIT TRANSACTION;
END
GO
