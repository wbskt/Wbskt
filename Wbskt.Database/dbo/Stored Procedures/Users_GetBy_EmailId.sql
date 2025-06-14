/* -------------------------------- */
/* Users_GetBy_EmailId              */
/* Author: Richard Joy              */
/* Updated by: Richard Joy          */
/* Create date: 24-Aug-2024         */
/* Description: Self explanatory    */
/* -------------------------------- */
CREATE PROCEDURE dbo.Users_GetBy_EmailId
  @EmailId VARCHAR(100)
AS
BEGIN
  SET NOCOUNT ON;

  SELECT Id
       , Name
       , EmailId
       , PasswordHash
       , PasswordSalt
    FROM dbo.Users
   WHERE EmailId = @EmailId
END;
