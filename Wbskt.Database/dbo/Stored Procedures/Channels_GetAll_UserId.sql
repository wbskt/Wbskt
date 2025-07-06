CREATE PROCEDURE dbo.Channels_GetAll_UserId
@LastModified   DATETIME,
@UserId         INT
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
            UserId          =   @UserId
    AND     LastModified    >=  @LastModified
END;
