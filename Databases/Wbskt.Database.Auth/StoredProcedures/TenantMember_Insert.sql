CREATE PROCEDURE dbo.TenantMember_Insert
    @TenantId INT,
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.TenantMembers WHERE TenantId = @TenantId AND UserId = @UserId)
    BEGIN
        INSERT INTO dbo.TenantMembers (TenantId, UserId)
        VALUES (@TenantId, @UserId);
    END
END
GO
