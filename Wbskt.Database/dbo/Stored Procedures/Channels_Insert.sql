CREATE PROCEDURE dbo.Channels_Insert
  @Id               INT             OUTPUT
, @Name             VARCHAR(100)
, @UserId           INT
, @ChannelRef  UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.Channels(
        Name,
        UserId,
        ChannelRef,
        LastModified
    )
    VALUES(
        @Name,
        @UserId,
        @ChannelRef,
        CURRENT_TIMESTAMP
        );
    SELECT @Id = SCOPE_IDENTITY();

END;
