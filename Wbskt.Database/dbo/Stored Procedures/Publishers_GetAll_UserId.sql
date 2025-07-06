CREATE PROCEDURE dbo.Publishers_GetAll_UserId
@LastModified   DATETIME,
@UserId         INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        Id,
        Name,
        PublisherRef,
        UserId,
        LastModified
    FROM    dbo.Publishers
    WHERE
            UserId          =   @UserId
    AND     LastModified    >=  @LastModified
END;
