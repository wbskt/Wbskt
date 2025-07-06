/* ---------------------------------- */
/* Clients_GetAll_ChannelId           */
/* Author: Richard Joy                */
/* Updated by: Richard Joy            */
/* Create date: 25-Aug-2024           */
/* Description: Get all clients for a specific channel */
/* ---------------------------------- */
CREATE PROCEDURE dbo.Clients_GetAll_ChannelId
  @ChannelId INT
AS
BEGIN
  SET NOCOUNT ON;

  SELECT C.Id
       , C.Name
       , C.UniqueRef
       , C.ServerId
       , C.UserId
    FROM dbo.Clients C 
    INNER JOIN dbo.ClientsChannels CC ON C.Id = CC.ClientId
   WHERE CC.ChannelId = @ChannelId
   ORDER BY C.Id;
END;
