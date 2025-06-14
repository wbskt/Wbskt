CREATE PROCEDURE dbo.ClientsChannels_SetForClient
    @ClientId INT,
    @ChannelIds dbo.IdListTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    -- Remove old relations not in the new list
    DELETE FROM dbo.ClientsChannels
    WHERE ClientId = @ClientId
      AND ChannelId NOT IN (SELECT Id FROM @ChannelIds);

    -- Insert new relations
    INSERT INTO dbo.ClientsChannels (ClientId, ChannelId)
    SELECT @ClientId, Id
    FROM @ChannelIds
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.ClientsChannels
        WHERE ClientId = @ClientId AND ChannelId = Id
    );
END
