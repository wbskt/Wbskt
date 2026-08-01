-- Lists the users in a tenant. Without this there is no way to discover a user reference, which
-- makes every assignment endpoint uncallable from a UI.
CREATE PROCEDURE dbo.TenantMember_GetAll
    @TenantId INT,
    @Search NVARCHAR(100) = NULL,
    @Skip INT = 0,
    @Take INT = 100,
    @TotalCount INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT @TotalCount = COUNT(*)
    FROM dbo.TenantMembers TM
    INNER JOIN dbo.Users U ON U.Id = TM.UserId
    WHERE TM.TenantId = @TenantId
      AND (@Search IS NULL OR U.Username LIKE '%' + @Search + '%' OR U.Email LIKE '%' + @Search + '%');

    SELECT
        U.RefId,
        U.Username,
        U.Email,
        U.IsActive
    FROM dbo.TenantMembers TM
    INNER JOIN dbo.Users U ON U.Id = TM.UserId
    WHERE TM.TenantId = @TenantId
      AND (@Search IS NULL OR U.Username LIKE '%' + @Search + '%' OR U.Email LIKE '%' + @Search + '%')
    ORDER BY U.Username
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
END
GO
