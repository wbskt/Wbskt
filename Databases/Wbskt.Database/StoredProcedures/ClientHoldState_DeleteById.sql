CREATE PROCEDURE dbo.ClientHoldState_DeleteById
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.ClientHoldStates WHERE Id = @Id;
END;
GO
