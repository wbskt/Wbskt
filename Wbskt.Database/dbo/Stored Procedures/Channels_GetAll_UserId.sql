/* -------------------------------- */
/* Channels_GetAll_UserId            */
/* Author: Richard Joy              */
/* Updated by: Richard Joy          */
/* Create date: 24-Aug-2024         */
/* Description: Self explanatory    */
/* -------------------------------- */
CREATE PROCEDURE dbo.Channels_GetAll_UserId
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id
       , ChannelName
       , ChannelSubscriberId
       , UserId
    FROM dbo.Channels
    WHERE UserId = @UserId
END;
