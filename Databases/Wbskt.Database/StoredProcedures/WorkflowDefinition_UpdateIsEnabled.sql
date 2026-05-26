CREATE PROCEDURE dbo.WorkflowDefinition_UpdateIsEnabled
    @Id         INT,
    @IsEnabled  BIT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.WorkflowDefinitions
       SET IsEnabled = @IsEnabled
     WHERE Id = @Id;
END;
GO
