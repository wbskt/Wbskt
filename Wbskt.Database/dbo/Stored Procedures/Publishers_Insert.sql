/* -------------------------------- */
/* Publishers_Insert                */
/* Author: Richard Joy              */
/* Updated by: Richard Joy          */
/* Create date: 24-Aug-2024         */
/* Description: Self explanatory    */
/* -------------------------------- */
CREATE PROCEDURE dbo.Publishers_Insert
  @Id                   INT OUTPUT
, @UserId               INT
, @PublisherRef         UNIQUEIDENTIFIER
, @Name                 VARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.Publishers
    ( UserId
    , PublisherRef
    , Name
    )
    VALUES
        ( @UserId
        , @PublisherRef
        , @Name
        );
    SELECT @Id = SCOPE_IDENTITY();

END;
