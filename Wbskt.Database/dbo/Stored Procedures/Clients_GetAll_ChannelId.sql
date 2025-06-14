/* ---------------------------------- */
/* ClientConnections_GetAll_ChannelId */
/* Author: Richard Joy                */
/* Updated by: Richard Joy            */
/* Create date: 25-Aug-2024           */
/* Description: Self explanatory      */
/* ---------------------------------- */
CREATE PROCEDURE dbo.ClientConnections_GetAll_ChannelId
  @ChannelId INT
AS
BEGIN
  SET NOCOUNT ON;

  SELECT Id
       , Name
       , UniqueRef
       , ServerId
       , UserId
    FROM dbo.Clients C INNER JOIN ClientsChannels CC ON C.Id = CC.ChannelId
   WHERE CC.ChannelId = @ChannelId
END;
