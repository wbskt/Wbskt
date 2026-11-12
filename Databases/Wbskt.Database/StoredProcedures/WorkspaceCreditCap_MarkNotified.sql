-- Records that @WorkspaceId was told it reached @Percent of its cap in the month starting @MonthStart
-- (CreditNotices_GetDue). Never lowers what is recorded for that month; a cap removed in the meantime
-- has no row to mark.
CREATE PROCEDURE dbo.WorkspaceCreditCap_MarkNotified
    @WorkspaceId INT,
    @MonthStart  DATE,
    @Percent     INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.WorkspaceCreditCaps
    SET NotifiedMonthStart = @MonthStart,
        NotifiedPercent = @Percent
    WHERE WorkspaceId = @WorkspaceId
      AND (NotifiedMonthStart IS NULL
           OR NotifiedMonthStart < @MonthStart
           OR (NotifiedMonthStart = @MonthStart AND NotifiedPercent < @Percent));
END
GO
