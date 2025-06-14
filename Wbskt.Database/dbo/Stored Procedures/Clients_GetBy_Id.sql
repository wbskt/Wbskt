/* -------------------------------- */
/* Clients_GetBy_Id       */
/* Author: Richard Joy              */
/* Updated by: Richard Joy          */
/* Create date: 26-Aug-2024         */
/* Description: Self explanatory    */
/* -------------------------------- */
CREATE PROCEDURE dbo.Clients_GetBy_Id
  @Id INT
AS
BEGIN
  SET NOCOUNT ON;

  SELECT Id
       , Name
       , UniqueRef
       , ServerId
       , UserId
    FROM dbo.Clients
   WHERE Id = @Id
END;
