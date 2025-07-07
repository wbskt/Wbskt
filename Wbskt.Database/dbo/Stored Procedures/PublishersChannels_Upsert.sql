/*
    Procedure: dbo.PublishersChannels_Upsert
    Purpose: Efficiently upserts (inserts or updates) a publisher-channel relationship.
    Parameters:
        - @PublisherId INT: The publisher Id
        - @ChannelId INT: The channel Id
    Returns: None (inserts or updates Deleted and LastModified fields)
    Author: Richard Joy
    Date: 2025-04-25
    Last Modified: 2025-04-25 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.PublishersChannels_Upsert
    @PublisherId INT,
    @ChannelId INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Use MERGE statement for efficient "upsert" operation
    MERGE INTO dbo.PublishersChannels AS Target
    USING (VALUES (@PublisherId, @ChannelId)) AS Source (PublisherId, ChannelId)
    ON (Target.PublisherId = Source.PublisherId AND Target.ChannelId = Source.ChannelId)
    WHEN MATCHED THEN
        -- If a matching record exists, update the 'Deleted' column to 0
        UPDATE SET Target.Deleted = 0, Target.LastModified = CURRENT_TIMESTAMP
    WHEN NOT MATCHED BY TARGET THEN
        -- If no matching record exists, insert a new one with Deleted = 0
        INSERT (PublisherId, ChannelId, Deleted)
        VALUES (Source.PublisherId, Source.ChannelId, 0);
END
