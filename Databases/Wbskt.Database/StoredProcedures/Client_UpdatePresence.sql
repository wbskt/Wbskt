CREATE PROCEDURE dbo.Client_UpdatePresence
    @Id INT,
    @IsConnected BIT,
    @LastActivityAt DATETIME2(0)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Clients
    SET 
        IsConnected = @IsConnected,
        LastActivityAt = @LastActivityAt
    WHERE Id = @Id;
END
GO
