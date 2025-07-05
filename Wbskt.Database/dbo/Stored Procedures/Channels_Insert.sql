CREATE PROCEDURE dbo.Channels_Insert
  @Id               INT             OUTPUT
, @Name             VARCHAR(100)
, @UserId           INT
, @SubscriptionRef  UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.Channels(
        Name,
        UserId,
        SubscriptionRef,
        LastModified
    )
    VALUES(
        @Name,
        @UserId,
        @SubscriptionRef,
        CURRENT_TIMESTAMP
        );
    SELECT @Id = SCOPE_IDENTITY();

END;
