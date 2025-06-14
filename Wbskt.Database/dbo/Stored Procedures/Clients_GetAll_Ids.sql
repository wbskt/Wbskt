/* -------------------------------- */
/* Clients_GetAll_Ids     */
/* Author: Richard Joy              */
/* Updated by: Richard Joy          */
/* Create date: 1-May-2025          */
/* Description: Get clients by IDs  */
/* -------------------------------- */
CREATE PROCEDURE dbo.Clients_GetAll_Ids
    @Ids dbo.IdListTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    SELECT C.Id,
           C.Name,
           C.UniqueRef,
           C.ServerId,
           C.UserId
    FROM dbo.Clients C
             INNER JOIN @Ids I ON C.Id = I.Id;
END;
