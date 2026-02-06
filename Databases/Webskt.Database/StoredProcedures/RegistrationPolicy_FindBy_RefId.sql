CREATE PROCEDURE dbo.RegistrationPolicy_FindBy_RefId
    @RefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        Id
    FROM dbo.RegistrationPolicies
    WHERE RefId = @RefId;
END
