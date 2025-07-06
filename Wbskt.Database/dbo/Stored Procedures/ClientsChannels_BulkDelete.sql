CREATE PROCEDURE dbo.ClientsChannels_BulkDelete
    @ClientChannelData ClientChannelTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    -- Soft delete by setting Deleted = 1 for all matching records
    UPDATE cc
    SET cc.Deleted = 1, cc.LastModified = CURRENT_TIMESTAMP 
    FROM dbo.ClientsChannels cc
    INNER JOIN @ClientChannelData AS Source 
        ON cc.ClientId = Source.ClientId 
        AND cc.ChannelId = Source.ChannelId;
END 