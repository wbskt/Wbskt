CREATE PROCEDURE [dbo].[WorkflowExecutions_Insert]
    @WorkflowId INT,
    @Status VARCHAR(20),
    @TriggeredAt DATETIME2,
    @InitialContext NVARCHAR(MAX),
    @Id INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO [dbo].[WorkflowExecutions] ([WorkflowId], [Status], [TriggeredAt], [InitialContext])
    VALUES (@WorkflowId, @Status, @TriggeredAt, @InitialContext);

    SET @Id = SCOPE_IDENTITY();
END
