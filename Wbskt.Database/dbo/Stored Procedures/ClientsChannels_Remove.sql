/*
    Procedure: dbo.ClientsChannels_Remove
    Purpose: Soft deletes (sets Deleted=1) a specific client-channel relationship.
    Parameters:
        - @ClientId INT: The client Id
        - @ChannelId INT: The channel Id
    Returns: None (updates Deleted and LastModified fields)
    Author: Richard Joy
    Date: 2025-04-25
    Last Modified: 2025-04-25 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.ClientsChannels_Remove
    @ClientId INT,
    @ChannelId INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.ClientsChannels
    SET Deleted = 1,
        LastModified = CURRENT_TIMESTAMP
    WHERE ClientId = @ClientId
      AND ChannelId = @ChannelId;
END;
