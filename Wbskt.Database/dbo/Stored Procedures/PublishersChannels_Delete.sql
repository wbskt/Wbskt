/*
    Procedure: dbo.PublishersChannels_Delete
    Purpose: Soft deletes (sets Deleted=1) a specific publisher-channel relationship.
    Parameters:
        - @PublisherId INT: The publisher Id
        - @ChannelId INT: The channel Id
    Returns: None (updates Deleted and LastModified fields)
    Author: Richard Joy
    Date: 2025-04-25
    Last Modified: 2025-04-25 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.PublishersChannels_Delete
    @PublisherId INT,
    @ChannelId INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.PublishersChannels
    SET Deleted = 1,
        LastModified = CURRENT_TIMESTAMP
    WHERE PublisherId = @PublisherId
      AND ChannelId = @ChannelId;
END; 