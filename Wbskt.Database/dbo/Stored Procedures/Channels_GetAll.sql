/* -------------------------------- */
/* Channels_GetAll                  */
/* Author: Richard Joy              */
/* Updated by: Richard Joy          */
/* Create date: 26-Aug-2024         */
/* Description: Self explanatory    */
/* -------------------------------- */
CREATE PROCEDURE dbo.Channels_GetAll
AS
BEGIN
  SET NOCOUNT ON;

  SELECT Id
       , Name
       , SubscriptionRef
       , UserId
    FROM dbo.Channels
END;
