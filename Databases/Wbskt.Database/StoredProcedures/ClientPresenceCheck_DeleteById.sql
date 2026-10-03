CREATE PROCEDURE dbo.ClientPresenceCheck_DeleteById
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.ClientPresenceChecks
    WHERE Id = @Id;
END;
GO
