/* -------------------------------- */
/* Clients_GetAll         */
/* Author: Richard Joy              */
/* Updated by: Richard Joy          */
/* Create date: 25-Aug-2024         */
/* Description: Self explanatory    */
/* -------------------------------- */
CREATE PROCEDURE dbo.Clients_GetAll
AS
BEGIN
  SET NOCOUNT ON;

  SELECT Id
       , Name
       , UniqueRef
       , ServerId
       , UserId
    FROM dbo.Clients
END;
