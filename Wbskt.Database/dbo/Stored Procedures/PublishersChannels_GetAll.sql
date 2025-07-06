CREATE PROCEDURE dbo.PublishersChannels_GetAll
@LastModified   DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        PublisherId,
        ChannelId,
        LastModified,
        Deleted
    FROM    dbo.PublishersChannels
    WHERE
            LastModified >= @LastModified
END; 