CREATE PROCEDURE [dbo].[Workflows_Delete_ByRefId]
    @RefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM [dbo].[Workflows]
    WHERE [RefId] = @RefId;
END
