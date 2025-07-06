/* ---------------------------------------------------------------- */
/* ClientsChannels_Add                                         */
/* Author: Richard Joy                                              */
/* Updated by: Richard Joy                                          */
/* Create date: 25-Apr-2025                                         */
/* Description: Insert or update a client based on ClientUniqueId   */
/* ---------------------------------------------------------------- */
CREATE PROCEDURE dbo.ClientsChannels_Add
    @ClientId   INT,
    @ChannelId  INT
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
END
