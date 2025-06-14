/* -------------------------------- */
/* Users_Insert                     */
/* Author: Richard Joy              */
/* Updated by: Richard Joy          */
/* Create date: 24-Aug-2024         */
/* Description: Self explanatory    */
/* -------------------------------- */
CREATE PROCEDURE dbo.Users_Insert
  @Id			INT OUTPUT
, @Name         VARCHAR(100)
, @EmailId		VARCHAR(100)
, @PasswordHash VARCHAR(512)
, @PasswordSalt VARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.Users
    ( Name
    , EmailId
    , PasswordHash
    , PasswordSalt
    )
    VALUES
        ( @Name
        , @EmailId
        , @PasswordHash
        , @PasswordSalt
        );
    SELECT @Id = SCOPE_IDENTITY();
END;
