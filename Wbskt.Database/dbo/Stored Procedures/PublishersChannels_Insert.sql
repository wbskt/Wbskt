/* -------------------------------- */
/* PublishersChannels_Insert        */
/* Author: Richard Joy              */
/* Updated by: Richard Joy          */
/* Create date: 24-Aug-2024         */
/* Description: Self explanatory    */
/* -------------------------------- */
CREATE PROCEDURE dbo.PublishersChannels_Insert
  @PublisherId  INT
, @ChannelId    INT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.PublishersChannels
            (PublisherId,   ChannelId)
    VALUES  (@PublisherId,  @ChannelId);

END;
