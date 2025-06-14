/* -------------------------------- */
/* Clients_FindBy_Name_UserId       */
/* Author: Richard Joy              */
/* Updated by: Richard Joy          */
/* Create date: 12-May-2025         */
/* Description: Self explanatory    */
/* -------------------------------- */
CREATE PROCEDURE dbo.Clients_FindBy_Name_UserId
    @UserId         INT,
    @Name     VARCHAR (100)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id
    FROM dbo.Clients
    WHERE UserId = @UserId AND Name = @Name;
END;
