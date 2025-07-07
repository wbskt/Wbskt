/*
    Procedure: dbo.Channels_Insert
    Purpose: Inserts a new channel and returns the generated Id.
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
CREATE PROCEDURE dbo.Channels_Insert
    @Id INT OUTPUT,
    @Name VARCHAR(100),
    @UserId INT,
    @ChannelRef UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.Channels (
        Name,
        UserId,
        ChannelRef,
        LastModified
    )
    VALUES (
        @Name,
        @UserId,
        @ChannelRef,
        CURRENT_TIMESTAMP
    );
    SELECT @Id = SCOPE_IDENTITY();
END;
