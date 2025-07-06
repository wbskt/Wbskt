CREATE PROCEDURE dbo.ClientsChannels_BulkUpsert
    @ClientChannelData ClientChannelTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    -- Use MERGE statement for efficient bulk "upsert" operation
    MERGE INTO dbo.ClientsChannels AS Target
    USING @ClientChannelData AS Source
    ON (Target.ClientId = Source.ClientId AND Target.ChannelId = Source.ChannelId)
    WHEN MATCHED THEN
        -- If a matching record exists, update the 'Deleted' column to 0
        UPDATE SET Target.Deleted = 0, Target.LastModified = CURRENT_TIMESTAMP
    WHEN NOT MATCHED BY TARGET THEN
        -- If no matching record exists, insert a new one with Deleted = 0
        INSERT (ClientId, ChannelId, Deleted)
        VALUES (Source.ClientId, Source.ChannelId, 0);
END 