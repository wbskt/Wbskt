-- Records that @TenantId was told it reached @Percent of its plan's allowance in the month starting
-- @MonthStart (CreditNotices_GetDue). Never lowers what is recorded.
CREATE PROCEDURE dbo.TenantCreditMonth_MarkNotified
    @TenantId   INT,
    @MonthStart DATE,
    @Percent    INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.TenantCreditMonths
    SET NotifiedPercent = @Percent
    WHERE TenantId = @TenantId
      AND MonthStart = @MonthStart
      AND NotifiedPercent < @Percent;
END
GO
