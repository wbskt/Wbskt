-- Gives back a notice recorded by dbo.WorkspaceMessageLimit_Record whose event could not be published,
-- so the next check records and announces it again: the one recorded against @Allowance.
CREATE PROCEDURE dbo.WorkspaceMessageLimit_Release
    @WorkspaceId INT,
    @MonthStart  DATE,
    @Percent     INT,
    @Allowance   BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.WorkspaceMessageLimitNotices
    WHERE WorkspaceId = @WorkspaceId
      AND MonthStart = @MonthStart
      AND [Percent] = @Percent
      AND Allowance = @Allowance;
END
GO
