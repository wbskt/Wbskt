CREATE PROCEDURE dbo.PublishersChannels_BulkUpsert
    @PublisherChannelData PublisherChannelTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    -- Use MERGE statement for efficient bulk "upsert" operation
    MERGE INTO dbo.PublishersChannels AS Target
    USING @PublisherChannelData AS Source
    ON (Target.PublisherId = Source.PublisherId AND Target.ChannelId = Source.ChannelId)
    WHEN MATCHED THEN
        -- If a matching record exists, update the 'Deleted' column to 0
        UPDATE SET Target.Deleted = 0, Target.LastModified = CURRENT_TIMESTAMP
    WHEN NOT MATCHED BY TARGET THEN
        -- If no matching record exists, insert a new one with Deleted = 0
        INSERT (PublisherId, ChannelId, Deleted)
        VALUES (Source.PublisherId, Source.ChannelId, 0);
END 