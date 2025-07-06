CREATE PROCEDURE dbo.ClientsChannels_GetAll_LastModified
    @LastModified DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT
        ClientId,
        ChannelId,
        LastModified,
        Deleted
    FROM dbo.ClientsChannels
    WHERE LastModified >= @LastModified
    ORDER BY ClientId, ChannelId;
END; 