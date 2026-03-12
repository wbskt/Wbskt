CREATE PROCEDURE dbo.RolePermission_Grant
    @RoleId INT,
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

MERGE dbo.RolePermissions AS target
        USING (SELECT @RoleId AS RoleId, @PermissionId AS PermissionId) AS source
        ON (target.RoleId = source.RoleId AND target.PermissionId = source.PermissionId)
        WHEN MATCHED THEN
            UPDATE SET IsDeny = @IsDeny
        WHEN NOT MATCHED THEN
            INSERT (
                RoleId, 
                PermissionId, 
                IsDeny
            )
            VALUES (
                @RoleId, 
                @PermissionId, 
                @IsDeny
            );
    END
END
GO
