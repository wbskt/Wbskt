CREATE PROCEDURE dbo.ClientsChannels_SetForClient
    @ClientId INT,
    @ChannelIds dbo.IdListTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Soft delete existing relations not in new list
    UPDATE dbo.ClientsChannels
    SET Deleted = 1, LastModified = CURRENT_TIMESTAMP
    WHERE ClientId = @ClientId AND Deleted = 0
      AND ChannelId NOT IN (SELECT Id FROM @ChannelIds);
    
    -- Insert new relations
    INSERT INTO dbo.ClientsChannels (ClientId, ChannelId, Deleted)
    SELECT @ClientId, Id, 0
    FROM @ChannelIds
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.ClientsChannels
        WHERE ClientId = @ClientId AND ChannelId = Id
    );
    
    -- Restore deleted relations
    UPDATE dbo.ClientsChannels
    SET Deleted = 0, LastModified = CURRENT_TIMESTAMP
    WHERE ClientId = @ClientId
      AND ChannelId IN (SELECT Id FROM @ChannelIds)
      AND Deleted = 1;
END
