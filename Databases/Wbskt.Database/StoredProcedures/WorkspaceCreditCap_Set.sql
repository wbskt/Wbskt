-- Sets @WorkspaceId's monthly credit cap, or removes it with @MonthlyCreditCap NULL. Returns the cap and
-- what the workspace has spent since @MonthStart, so a caller lowering the cap below that can say new
-- runs are refused at once.
CREATE PROCEDURE dbo.WorkspaceCreditCap_Set
    @WorkspaceId      INT,
    @MonthlyCreditCap DECIMAL(18,4),
    @MonthStart       DATE
AS
BEGIN
    SET NOCOUNT ON;

    IF @MonthlyCreditCap IS NULL
    BEGIN
        DELETE FROM dbo.WorkspaceCreditCaps WHERE WorkspaceId = @WorkspaceId;
    END
    ELSE
    BEGIN
        MERGE dbo.WorkspaceCreditCaps WITH (HOLDLOCK) AS t
        USING (SELECT @WorkspaceId AS WorkspaceId) AS s
        ON t.WorkspaceId = s.WorkspaceId
        WHEN MATCHED THEN
            UPDATE SET MonthlyCreditCap = @MonthlyCreditCap, UpdatedAt = SYSUTCDATETIME()
        WHEN NOT MATCHED THEN
            INSERT (WorkspaceId, MonthlyCreditCap) VALUES (@WorkspaceId, @MonthlyCreditCap);
    END

    SELECT
        @MonthlyCreditCap AS MonthlyCreditCap,
        s.Credits AS CreditsSpent
    FROM dbo.WorkspaceCreditsSpent(@WorkspaceId, @MonthStart) s;
END;
GO
