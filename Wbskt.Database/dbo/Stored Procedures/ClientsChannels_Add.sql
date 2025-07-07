/*
    Procedure: dbo.ClientsChannels_Add
    Purpose: Adds a client-channel relationship if it does not already exist.
    Parameters:
        - @ClientId INT: The client Id
        - @ChannelId INT: The channel Id
    Returns: None (inserts new record if not present)
    Author: Richard Joy
    Date: 2025-04-25
    Last Modified: 2025-04-25 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.ClientsChannels_Add
    @ClientId INT,
    @ChannelId INT
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (
        SELECT 1 FROM dbo.ClientsChannels WHERE ClientId = @ClientId AND ChannelId = @ChannelId
    )
    BEGIN
        INSERT INTO dbo.ClientsChannels (ClientId, ChannelId, Deleted)
        VALUES (@ClientId, @ChannelId, 0);
    END
END;
