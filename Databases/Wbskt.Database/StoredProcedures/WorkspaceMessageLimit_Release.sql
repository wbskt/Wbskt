-- Gives back a notice recorded by dbo.WorkspaceMessageLimit_Record whose event could not be published,
-- so the next check records and announces it again. @Allowance is NULL from a management host on the
-- earlier build, which gives back the notice whatever its allowance; remove that in the release after.
CREATE PROCEDURE dbo.WorkspaceMessageLimit_Release
    @WorkspaceId INT,
    @MonthStart  DATE,
    @Percent     INT,
    @Allowance   BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.WorkspaceMessageLimitNotices
    WHERE WorkspaceId = @WorkspaceId
      AND MonthStart = @MonthStart
      AND [Percent] = @Percent
      AND (@Allowance IS NULL OR Allowance = @Allowance);
END
GO
