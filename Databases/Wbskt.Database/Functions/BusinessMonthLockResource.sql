-- The application lock a month's Business usage is written under (sp_getapplock), by
-- dbo.BusinessTenantMonth_Track and dbo.BusinessMonth_Close, so a close never meets a track half done.
CREATE FUNCTION dbo.BusinessMonthLockResource (@MonthStart DATE)
RETURNS NVARCHAR(255)
WITH SCHEMABINDING
AS
BEGIN
    RETURN CONCAT(N'business-month:', CONVERT(NCHAR(10), @MonthStart, 23));
END
GO
