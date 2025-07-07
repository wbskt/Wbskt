/*
    Procedure: dbo.Channels_Insert_Seq
    Purpose: Inserts a new channel, creates a publisher with the same reference, and links them. Returns the new channel Id.
    Parameters:
        - @Id INT OUTPUT: Returns the new channel Id
        - @Name VARCHAR(100): Channel name
        - @UserId INT: User Id (owner)
        - @ChannelRef UNIQUEIDENTIFIER: Unique channel reference
    Returns: None (output parameter @Id is set)
    Author: Richard Joy
    Date: 2024-08-24
    Last Modified: 2024-08-24 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.Channels_Insert_Seq
    @Id INT OUTPUT,
    @Name VARCHAR(100),
    @UserId INT,
    @ChannelRef UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.Channels_Insert
        @Id = @Id OUTPUT,
        @Name = @Name,
        @UserId = @UserId,
        @ChannelRef = @ChannelRef;

    DECLARE @PublisherId INT;

    EXEC dbo.Publishers_Insert
        @Id = @PublisherId OUTPUT,
        @UserId = @UserId,
        @PublisherRef = @ChannelRef,
        @Name = @Name;

    EXEC dbo.PublishersChannels_Upsert
        @ChannelId = @Id,
        @PublisherId = @PublisherId;
END;
