CREATE PROCEDURE [dbo].[Workflows_Update]
    @RefId UNIQUEIDENTIFIER,
    @Name NVARCHAR(100),
    @Description NVARCHAR(500),
    @IsEnabled BIT,
    @TriggerType INT,
    @TriggerConfiguration NVARCHAR(MAX),
    @ViewportX FLOAT,
    @ViewportY FLOAT,
    @ViewportZoom FLOAT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE [dbo].[Workflows]
    SET [Name] = @Name,
        [Description] = @Description,
        [IsEnabled] = @IsEnabled,
        [TriggerType] = @TriggerType,
        [TriggerConfiguration] = @TriggerConfiguration,
        [ViewportX] = @ViewportX,
        [ViewportY] = @ViewportY,
        [ViewportZoom] = @ViewportZoom,
        [LastModified] = GETUTCDATE()
    WHERE [RefId] = @RefId;
END
