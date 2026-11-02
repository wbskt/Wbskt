-- The types one client has sent, most recently seen first.
CREATE PROCEDURE dbo.ClientMessageTypes_GetBy_ClientId
    @ClientId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Type, MessageCount, FirstSeenAt, LastSeenAt, SamplePayload
    FROM dbo.ClientMessageTypes
    WHERE ClientId = @ClientId
    ORDER BY LastSeenAt DESC, Type;
END
GO
