CREATE PROCEDURE dbo.WorkspaceAccount_FindBy_WorkspaceId
    @WorkspaceId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        WorkspaceId,
        TenantId,
        TenantRefId,
        PlanCode,
        LimitOverrides,
        PlanRevision
    FROM dbo.WorkspaceAccounts
    WHERE WorkspaceId = @WorkspaceId;
END
GO
