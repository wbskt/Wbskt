/* ---------------------------------------- */
/* Channels_GetBy_ChannelSubscriptionRef    */
/* Author: Richard Joy                      */
/* Updated by: Richard Joy                  */
/* Create date: 19-Apr-2025                 */
/* Description: Self explanatory            */
/* ---------------------------------------- */
CREATE PROCEDURE dbo.Channels_GetBy_ChannelSubscriptionRef
    @SubscriptionRef UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id
         , Name
         , SubscriptionRef
         , UserId
    FROM dbo.Channels
    WHERE SubscriptionRef = @SubscriptionRef
END;
