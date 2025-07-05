CREATE PROCEDURE dbo.Publishers_GetAll
@LastModified   DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        Id,
        Name,
        UserId,
        LastModified,
        PublisherRef
    FROM    dbo.Publishers
    WHERE
            LastModified >= @LastModified
END;
