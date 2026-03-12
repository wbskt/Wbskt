CREATE PROCEDURE dbo.UserPermission_Grant
    @UserId INT,
    @PermissionSlug NVARCHAR(100),
    @IsDeny BIT
AS
BEGIN
    SET NOCOUNT ON;

DECLARE @PermissionId INT;
    SELECT @PermissionId = Id FROM dbo.Permissions WHERE Slug = @PermissionSlug;

    IF @PermissionId IS NOT NULL
    BEGIN
    SET NOCOUNT ON;

MERGE dbo.UserPermissions AS target
        USING (SELECT @UserId AS UserId, @PermissionId AS PermissionId) AS source
        ON (target.UserId = source.UserId AND target.PermissionId = source.PermissionId)
        WHEN MATCHED THEN
            UPDATE SET IsDeny = @IsDeny
        WHEN NOT MATCHED THEN
            INSERT (
                UserId, 
                PermissionId, 
                IsDeny
            )
            VALUES (
                @UserId, 
                @PermissionId, 
                @IsDeny
            );
    END
END
GO
