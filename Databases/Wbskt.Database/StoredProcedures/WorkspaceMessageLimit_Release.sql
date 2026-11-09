-- Gives back a notice recorded by dbo.WorkspaceMessageLimit_Record whose event could not be published,
-- so the next check records and announces it again.
CREATE PROCEDURE dbo.WorkspaceMessageLimit_Release
    @WorkspaceId INT,
    @MonthStart  DATE,
    @Percent     INT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.WorkspaceMessageLimitNotices
    WHERE WorkspaceId = @WorkspaceId
      AND MonthStart = @MonthStart
      AND [Percent] = @Percent;
END
GO
