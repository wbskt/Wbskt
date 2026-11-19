-- Freezes a tenant for staff, or lifts the freeze. Only the flag: what a freeze stops (changes in its
-- workspaces, runs, its clients) follows from dbo.Workspace_ResolveAccess and from the event the auth
-- host publishes once this has committed.
--
-- 50049: the tenant is already in that state, so there is nothing to announce.
CREATE PROCEDURE dbo.Tenant_StaffSetFrozen
    @TenantId INT,
    @Frozen BIT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Tenants
    SET FrozenAt = IIF(@Frozen = 1, SYSUTCDATETIME(), NULL)
    WHERE Id = @TenantId
      AND ((@Frozen = 1 AND FrozenAt IS NULL) OR (@Frozen = 0 AND FrozenAt IS NOT NULL));

    IF @@ROWCOUNT = 0
    BEGIN
        THROW 50049, 'The tenant is already in that state.', 1;
    END
END
GO
