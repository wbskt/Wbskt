/*
    Procedure: dbo.ClientsChannels_GetAll
    Purpose: Retrieves all client-channel relationships modified since the specified date/time.
    Parameters:
        - @LastModified DATETIME: Only return records modified on or after this timestamp
    Returns: ClientId, ChannelId, LastModified, Deleted
    Author: Richard Joy
    Date: 2025-04-25
    Last Modified: 2025-04-25 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.ClientsChannels_GetAll
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
    WHERE LastModified >= @LastModified;
END;
