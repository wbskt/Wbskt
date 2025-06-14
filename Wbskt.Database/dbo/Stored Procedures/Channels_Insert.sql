/* -------------------------------- */
/* Channels_Insert                  */
/* Author: Richard Joy              */
/* Updated by: Richard Joy          */
/* Create date: 24-Aug-2024         */
/* Description: Self explanatory    */
/* -------------------------------- */
CREATE PROCEDURE dbo.Channels_Insert
  @Id               INT             OUTPUT
, @Name             VARCHAR(100)
, @UserId           INT
, @SubscriptionRef  UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.Channels
    ( Name
    , UserId
    , SubscriptionRef
    )
    VALUES
        ( @Name
        , @UserId
        , @SubscriptionRef
        );
    SELECT @Id = SCOPE_IDENTITY();


    DECLARE @@PublisherId INT;

    EXEC dbo.Publishers_Insert
        @Id = @@PublisherId OUTPUT,
        @UserId = @UserId,
        @PublisherRef = @SubscriptionRef,
        @Name = @Name;

    EXEC dbo.PublishersChannels_Insert
        @ChannelId = @Id,
        @PublisherId = @@PublisherId

END;
