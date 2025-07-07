/*
    Procedure: dbo.PublishersChannels_GetAll
    Purpose: Retrieves all publisher-channel relationships modified since the specified date/time.
    Parameters:
        - @LastModified DATETIME: Only return records modified on or after this timestamp
    Returns: PublisherId, ChannelId, LastModified, Deleted
    Author: Richard Joy
    Date: 2025-04-25
    Last Modified: 2025-04-25 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.PublishersChannels_GetAll
    @LastModified DATETIME
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        PublisherId,
        ChannelId,
        LastModified,
        Deleted
    FROM dbo.PublishersChannels
    WHERE LastModified >= @LastModified;
END; 