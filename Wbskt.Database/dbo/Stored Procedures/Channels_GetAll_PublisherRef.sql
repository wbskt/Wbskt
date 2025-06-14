/* ------------------------------------ */
/* Channels_GetAll_PublisherRef       */
/* Author: Richard Joy                  */
/* Updated by: Richard Joy              */
/* Create date: 25-Aug-2024             */
/* Description: Self explanatory        */
/* ------------------------------------ */
CREATE PROCEDURE dbo.Channels_GetAll_PublisherRef
    @PublisherId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT  Id,
            Name,
            SubscriptionRef,
            UserId
    FROM    dbo.Channels C INNER JOIN dbo.PublishersChannels PC on C.Id = PC.ChannelId
    WHERE   PC.PublisherId = @PublisherId
END;
