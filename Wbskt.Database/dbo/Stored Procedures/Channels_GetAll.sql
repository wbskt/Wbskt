/*
    Procedure: dbo.Channels_GetAll
    Purpose: Retrieves all channels modified since the specified date/time.
    Parameters:
        - @LastModified DATETIME: Only return channels modified on or after this timestamp
    Returns: Id, Name, ChannelRef, UserId, LastModified
    Author: Richard Joy
    Date: 2025-04-25
    Last Modified: 2025-04-25 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.Channels_GetAll
    @LastModified DATETIME
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Id,
        Name,
        ChannelRef,
        UserId,
        LastModified
    FROM dbo.Channels
    WHERE LastModified >= @LastModified;
END;
