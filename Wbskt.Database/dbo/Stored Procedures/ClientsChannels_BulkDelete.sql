/*
    Procedure: dbo.ClientsChannels_BulkDelete
    Purpose: Soft deletes (sets Deleted=1) all matching client-channel records in bulk.
    Parameters:
        - @ClientChannelData ClientChannelTableType READONLY: Table of ClientId/ChannelId pairs
    Returns: None (updates Deleted and LastModified fields)
    Author: Richard Joy
    Date: 2025-04-25
    Last Modified: 2025-04-25 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.ClientsChannels_BulkDelete
    @ClientChannelData ClientChannelTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    -- Soft delete by setting Deleted = 1 for all matching records
    UPDATE cc
    SET cc.Deleted = 1,
        cc.LastModified = CURRENT_TIMESTAMP
    FROM dbo.ClientsChannels cc
    INNER JOIN @ClientChannelData AS Source
        ON cc.ClientId = Source.ClientId
        AND cc.ChannelId = Source.ChannelId;
END; 