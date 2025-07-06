CREATE PROCEDURE dbo.Channels_GetAll
@LastModified   DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        Id,
        Name,
        ChannelRef,
        UserId,
        LastModified
    FROM    dbo.Channels
    WHERE
            LastModified >= @LastModified
END;
