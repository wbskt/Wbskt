CREATE PROCEDURE dbo.Channels_Insert_Seq
  @Id               INT             OUTPUT
, @Name             VARCHAR(100)
, @UserId           INT
, @SubscriptionRef  UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.Channels_Insert
        @Id                 = @Id OUTPUT,
        @Name               = @Name,
        @UserId             = @UserId,
        @SubscriptionRef    = @SubscriptionRef

    DECLARE @PublisherId INT;

    EXEC dbo.Publishers_Insert
         @Id                = @PublisherId OUTPUT,
         @UserId            = @UserId,
         @PublisherRef      = @SubscriptionRef,
         @Name              = @Name;

    EXEC dbo.PublishersChannels_Insert
         @ChannelId         = @Id,
         @PublisherId       = @PublisherId

END;
