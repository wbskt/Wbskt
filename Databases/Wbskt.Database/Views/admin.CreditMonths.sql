-- Each account's monthly credit allowance and how much of it was drawn.
CREATE VIEW [admin].CreditMonths
AS
SELECT
    CM.TenantId,
    CM.MonthStart,
    CM.Posted,
    CM.Drawn
FROM dbo.TenantCreditMonths CM;
GO
