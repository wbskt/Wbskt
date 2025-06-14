/* -------------------------------- */
/* Channels_GetBy_Id                */
/* Author: Richard Joy              */
/* Updated by: Richard Joy          */
/* Create date: 24-Aug-2024         */
/* Description: Self explanatory    */
/* -------------------------------- */
CREATE PROCEDURE dbo.Channels_GetBy_Id
  @Id INT
AS
BEGIN
  SET NOCOUNT ON;

  SELECT Id
       , Name
       , SubscriptionRef
       , UserId
    FROM dbo.Channels
   WHERE Id = @Id
END;
