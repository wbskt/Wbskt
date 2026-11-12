-- Records that @WorkspaceId was told it reached @Percent of its cap in the month starting @MonthStart
-- (CreditNotices_GetDue) of the cap @Limit. Never lowers what is recorded for that month; a cap removed
-- in the meantime has no row to mark, and one raised past @Limit since (which WorkspaceCreditCap_Set
-- resets) is left for the next check to work out against the new cap.
CREATE PROCEDURE dbo.WorkspaceCreditCap_MarkNotified
    @WorkspaceId INT,
    @MonthStart  DATE,
    @Percent     INT,
    @Limit       DECIMAL(18,4)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.WorkspaceCreditCaps
    SET NotifiedMonthStart = @MonthStart,
        NotifiedPercent = @Percent
    WHERE WorkspaceId = @WorkspaceId
      AND MonthlyCreditCap <= @Limit
      AND (NotifiedMonthStart IS NULL
           OR NotifiedMonthStart < @MonthStart
           OR (NotifiedMonthStart = @MonthStart AND NotifiedPercent < @Percent));
END
GO
