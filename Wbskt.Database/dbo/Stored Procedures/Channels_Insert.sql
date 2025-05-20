/* -------------------------------- */
/* Channels_Insert                  */
/* Author: Richard Joy              */
/* Updated by: Richard Joy          */
/* Create date: 24-Aug-2024         */
/* Description: Self explanatory    */
/* -------------------------------- */
CREATE PROCEDURE dbo.Channels_Insert
  @Id                   INT OUTPUT
, @ChannelName	        VARCHAR(100)
, @UserId               INT
, @ChannelSubscriberId  UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.Channels
    ( ChannelName
    , UserId
    , ChannelSubscriberId
    )
    VALUES
        ( @ChannelName
        , @UserId
        , @ChannelSubscriberId
        );
    SELECT @Id = SCOPE_IDENTITY();
END;
