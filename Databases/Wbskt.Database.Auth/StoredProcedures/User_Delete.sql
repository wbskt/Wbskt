-- Deletes an account at its owner's request (POST /api/auth/account/delete), in one transaction:
--
-- * A tenant where the user is the only member goes with them: its workspaces (through
--   dbo.Workspace_Delete, so each is recorded in dbo.DeletedWorkspaces and its WorkspaceDeletedEvent,
--   passed in @DeletedWorkspaces, goes into the outbox for the management host to retire), roles,
--   groups, invitations and the tenant row.
-- * A tenant with other members loses only this member, through dbo.TenantMember_Remove: workspaces
--   they own pass to another administrator of that tenant. Refused (THROW 50018) while nobody else
--   administers it: deleting them would leave the others in a tenant nobody can administer, so
--   someone else must be made an administrator first. The message names those tenants.
-- * Every session ends: refresh tokens, reset and verification tokens are deleted, along with the
--   addresses they were requested from.
-- * The user row stays, anonymised, because invitations and event logs refer to it by id and RefId:
--   username and email become deleted-<RefId>, the password hash is cleared and the account is
--   inactive, so nothing can sign in as it and the address can register again. Invitations sent to
--   the address have it replaced the same way, and any still pending are revoked.
--
-- @DeletedWorkspaces is a JSON array of { workspaceId, messageType, payload }, one per workspace
-- dbo.User_GetSoleTenantWorkspaces returned. If membership changed since then, so that the list no
-- longer matches, nothing is deleted (THROW 50019) and the caller can try again.
CREATE PROCEDURE dbo.User_Delete
    @UserId INT,
    @DeletedWorkspaces NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    DECLARE @Tenants TABLE (TenantId INT PRIMARY KEY, IsSole BIT NOT NULL);

    -- Lock every tenant the user belongs to first, as every procedure that guards the last
    -- administrator does (see dbo.Tenant_Administrators), then read membership under the locks.
    -- HOLDLOCK on the membership read as well: accepting an invitation does not lock the tenant row,
    -- so without the key-range lock someone could join a tenant already judged to be the user's alone.
    DECLARE @Locked INT;
    SELECT @Locked = COUNT(*)
    FROM dbo.Tenants T WITH (UPDLOCK, HOLDLOCK)
    WHERE T.Id IN (SELECT TenantId FROM dbo.TenantMembers WHERE UserId = @UserId);

    INSERT INTO @Tenants (TenantId, IsSole)
    SELECT TM.TenantId,
           IIF(EXISTS (SELECT 1 FROM dbo.TenantMembers O WITH (HOLDLOCK) WHERE O.TenantId = TM.TenantId AND O.UserId <> @UserId), 0, 1)
    FROM dbo.TenantMembers TM WITH (HOLDLOCK)
    WHERE TM.UserId = @UserId;

    DECLARE @Blocking NVARCHAR(2000) = (
        SELECT STRING_AGG(CAST(TN.Name AS NVARCHAR(MAX)), N', ') WITHIN GROUP (ORDER BY TN.Name)
        FROM @Tenants T
        INNER JOIN dbo.Tenants TN ON TN.Id = T.TenantId
        WHERE T.IsSole = 0
          AND NOT EXISTS (SELECT 1 FROM dbo.Tenant_Administrators(T.TenantId) A WHERE A.UserId <> @UserId));

    IF @Blocking IS NOT NULL
    BEGIN
        DECLARE @BlockingMessage NVARCHAR(2048) = LEFT(
            N'Nobody else administers ' + @Blocking + N'. Make another member an administrator, or remove the other members, before deleting your account.', 2048);
        THROW 50018, @BlockingMessage, 1;
    END

    DECLARE @Planned TABLE (WorkspaceId INT PRIMARY KEY, MessageType NVARCHAR(256) NOT NULL, Payload NVARCHAR(MAX) NOT NULL);
    INSERT INTO @Planned (WorkspaceId, MessageType, Payload)
    SELECT workspaceId, messageType, payload
    FROM OPENJSON(@DeletedWorkspaces)
    WITH (workspaceId INT, messageType NVARCHAR(256), payload NVARCHAR(MAX));

    IF EXISTS (
            SELECT W.Id FROM dbo.Workspaces W INNER JOIN @Tenants T ON T.TenantId = W.TenantId AND T.IsSole = 1
            EXCEPT
            SELECT WorkspaceId FROM @Planned)
        OR EXISTS (
            SELECT WorkspaceId FROM @Planned
            EXCEPT
            SELECT W.Id FROM dbo.Workspaces W INNER JOIN @Tenants T ON T.TenantId = W.TenantId AND T.IsSole = 1)
    BEGIN
        THROW 50019, 'Your tenants changed while the account was being deleted. Try again.', 1;
    END

    -- Tenants shared with others: leave them.
    DECLARE @TenantId INT, @NewOwnerUserId INT;
    DECLARE shared CURSOR LOCAL FAST_FORWARD FOR SELECT TenantId FROM @Tenants WHERE IsSole = 0;
    OPEN shared;
    FETCH NEXT FROM shared INTO @TenantId;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        -- There is one: the guard above refused every shared tenant without another administrator.
        SET @NewOwnerUserId = (SELECT MIN(UserId) FROM dbo.Tenant_Administrators(@TenantId) WHERE UserId <> @UserId);

        EXEC dbo.TenantMember_Remove @TenantId = @TenantId, @UserId = @UserId, @NewOwnerUserId = @NewOwnerUserId;

        FETCH NEXT FROM shared INTO @TenantId;
    END
    CLOSE shared;
    DEALLOCATE shared;

    -- Tenants only they belong to: delete them.
    DECLARE @WorkspaceId INT, @MessageType NVARCHAR(256), @Payload NVARCHAR(MAX);
    DECLARE planned CURSOR LOCAL FAST_FORWARD FOR SELECT WorkspaceId, MessageType, Payload FROM @Planned;
    OPEN planned;
    FETCH NEXT FROM planned INTO @WorkspaceId, @MessageType, @Payload;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        EXEC dbo.Workspace_Delete @Id = @WorkspaceId, @OutboxMessageType = @MessageType, @OutboxPayload = @Payload;
        FETCH NEXT FROM planned INTO @WorkspaceId, @MessageType, @Payload;
    END
    CLOSE planned;
    DEALLOCATE planned;

    DECLARE @Sole TABLE (TenantId INT PRIMARY KEY);
    INSERT INTO @Sole (TenantId) SELECT TenantId FROM @Tenants WHERE IsSole = 1;

    DELETE FROM dbo.UserRoles WHERE TenantId IN (SELECT TenantId FROM @Sole);
    DELETE FROM dbo.UserPermissions WHERE TenantId IN (SELECT TenantId FROM @Sole);
    DELETE FROM dbo.GroupRoles WHERE TenantId IN (SELECT TenantId FROM @Sole);
    DELETE UG FROM dbo.UserGroups UG INNER JOIN dbo.Groups G ON G.Id = UG.GroupId WHERE G.TenantId IN (SELECT TenantId FROM @Sole);
    UPDATE dbo.Groups SET ParentGroupId = NULL WHERE TenantId IN (SELECT TenantId FROM @Sole) AND ParentGroupId IS NOT NULL;
    DELETE FROM dbo.Groups WHERE TenantId IN (SELECT TenantId FROM @Sole);
    -- Invitations go before roles: each names the role it grants. Their workspace rows cascade.
    DELETE FROM dbo.TenantInvitations WHERE TenantId IN (SELECT TenantId FROM @Sole);
    DELETE RP FROM dbo.RolePermissions RP INNER JOIN dbo.Roles R ON R.Id = RP.RoleId WHERE R.TenantId IN (SELECT TenantId FROM @Sole);
    DELETE FROM dbo.Roles WHERE TenantId IN (SELECT TenantId FROM @Sole);
    -- TenantMembers cascades.
    DELETE FROM dbo.Tenants WHERE Id IN (SELECT TenantId FROM @Sole);

    -- Anything left that names the user: assignments without a tenant row of theirs, sessions, tokens.
    DELETE FROM dbo.UserRoles WHERE UserId = @UserId;
    DELETE FROM dbo.UserPermissions WHERE UserId = @UserId;
    DELETE FROM dbo.UserGroups WHERE UserId = @UserId;
    DELETE FROM dbo.WorkspaceMembers WHERE UserId = @UserId;
    DELETE FROM dbo.RefreshTokens WHERE UserId = @UserId;
    DELETE FROM dbo.PasswordResetTokens WHERE UserId = @UserId;
    DELETE FROM dbo.EmailVerificationTokens WHERE UserId = @UserId;

    DECLARE @Placeholder NVARCHAR(40) = N'deleted-' + REPLACE(LOWER(CONVERT(NVARCHAR(36), (SELECT RefId FROM dbo.Users WHERE Id = @UserId))), N'-', N'');
    DECLARE @Email NVARCHAR(100) = (SELECT Email FROM dbo.Users WHERE Id = @UserId);

    UPDATE dbo.TenantInvitations
    SET RevokedAt = IIF(AcceptedAt IS NULL AND RevokedAt IS NULL, SYSUTCDATETIME(), RevokedAt),
        Email = @Placeholder + N'@deleted.invalid'
    WHERE Email = @Email;

    UPDATE dbo.Users
    SET Username = @Placeholder,
        Email = @Placeholder + N'@deleted.invalid',
        PasswordHash = N'',
        IsActive = 0,
        IsEmailVerified = 0,
        FailedLoginCount = 0,
        LockedUntil = NULL,
        LastLoginAt = NULL,
        PasswordChangedAt = NULL
    WHERE Id = @UserId;

    COMMIT TRANSACTION;

    -- The shared tenants the user left, for the caller to announce the departure in.
    SELECT TenantId FROM @Tenants WHERE IsSole = 0 ORDER BY TenantId;
END
GO
