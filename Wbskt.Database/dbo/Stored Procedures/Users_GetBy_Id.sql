/* -------------------------------- */
/* Users_GetBy_Id                   */
/* Author: Richard Joy              */
/* Updated by: Richard Joy          */
/* Create date: 24-Aug-2024         */
/* Description: Self explanatory    */
/* -------------------------------- */
CREATE PROCEDURE dbo.Users_GetBy_Id
  @Id INT
AS
BEGIN
  SET NOCOUNT ON;

  SELECT Id
       , Name
       , EmailId
       , PasswordHash
       , PasswordSalt
    FROM dbo.Users
   WHERE Id = @Id
END;
