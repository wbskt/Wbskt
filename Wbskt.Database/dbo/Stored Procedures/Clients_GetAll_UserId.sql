/* ------------------------------------ */
/* Clients_GetAll_UserId      */
/* Author: Richard Joy                  */
/* Updated by: Richard Joy              */
/* Create date: 25-Aug-2024             */
/* Description: Self explanatory        */
/* ------------------------------------ */
CREATE PROCEDURE dbo.Clients_GetAll_UserId
  @UserId INT
AS
BEGIN
  SET NOCOUNT ON;

  SELECT Id
       , Name
       , UniqueRef
       , ServerId
       , UserId
    FROM dbo.Clients
   WHERE UserId = @UserId
END;
