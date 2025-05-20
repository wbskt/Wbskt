/* -------------------------------- */
/* Channels_GetAll_ServerIds        */
/* Author: Richard Joy              */
/* Updated by: Richard Joy          */
/* Create date: 26-Aug-2024         */
/* Description: Self explanatory    */
/* -------------------------------- */
CREATE PROCEDURE dbo.Channels_GetAll_ServerIds
    @Ids  dbo.IdListTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    SELECT  Id,
            ChannelName,
            ChannelSubscriberId,
            UserId
    FROM    dbo.Channels C INNER JOIN dbo.ServersChannels SC on C.Id = SC.ChannelId
    WHERE   SC.ServerId IN (SELECT Id FROM @Ids);
END;
