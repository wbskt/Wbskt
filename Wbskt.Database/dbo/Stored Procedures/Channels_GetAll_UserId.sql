/*
    Procedure: dbo.Channels_GetAll_UserId
    Purpose: Retrieves all channels for a specific user modified since the specified date/time.
    Parameters:
        - @LastModified DATETIME: Only return channels modified on or after this timestamp
        - @UserId INT: Only return channels for this user
    Returns: Id, Name, ChannelRef, UserId, LastModified
    Author: Richard Joy
    Date: 2025-04-25
    Last Modified: 2025-04-25 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.Channels_GetAll_UserId
    @LastModified DATETIME,
    @UserId INT
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
    WHERE UserId = @UserId
      AND LastModified >= @LastModified;
END;
