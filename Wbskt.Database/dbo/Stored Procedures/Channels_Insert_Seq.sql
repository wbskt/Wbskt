CREATE PROCEDURE dbo.Channels_Insert_Seq
  @Id               INT             OUTPUT
, @Name             VARCHAR(100)
, @UserId           INT
, @ChannelRef  UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.Channels_Insert
        @Id                 = @Id OUTPUT,
        @Name               = @Name,
        @UserId             = @UserId,
        @ChannelRef         = @ChannelRef

    DECLARE @PublisherId INT;

    EXEC dbo.Publishers_Insert
         @Id                = @PublisherId OUTPUT,
         @UserId            = @UserId,
         @PublisherRef      = @ChannelRef,
         @Name              = @Name;

    EXEC dbo.PublishersChannels_Upsert
         @ChannelId         = @Id,
         @PublisherId       = @PublisherId

END;
