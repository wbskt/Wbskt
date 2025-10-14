CREATE PROCEDURE [dbo].[WorkflowExecutions_Insert]
    @WorkflowRefId UNIQUEIDENTIFIER,
    @Status VARCHAR(20),
    @TriggeredAt DATETIME2,
    @InitialContext NVARCHAR(MAX),
    @Id INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO [dbo].[WorkflowExecutions] ([WorkflowRefId], [Status], [TriggeredAt], [InitialContext])
    VALUES (@WorkflowRefId, @Status, @TriggeredAt, @InitialContext);

    SET @Id = SCOPE_IDENTITY();
END
