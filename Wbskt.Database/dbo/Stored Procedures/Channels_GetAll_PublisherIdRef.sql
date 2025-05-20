/* ------------------------------------ */
/* Channels_GetAll_PublisherIdRef       */
/* Author: Richard Joy                  */
/* Updated by: Richard Joy              */
/* Create date: 25-Aug-2024             */
/* Description: Self explanatory        */
/* ------------------------------------ */
CREATE PROCEDURE dbo.Channels_GetAll_PublisherIdRef
    @PublisherRef UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT  Id,
            ChannelName,
            ChannelSubscriberId,
            UserId
    FROM    dbo.Channels C INNER JOIN dbo.PublisherChannels PC on C.Id = PC.ChannelId
    WHERE   PC.PublisherRef = @PublisherRef
END;
