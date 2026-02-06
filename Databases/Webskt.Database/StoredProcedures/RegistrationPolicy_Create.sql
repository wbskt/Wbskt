CREATE PROCEDURE dbo.RegistrationPolicy_Create
    @Name NVARCHAR(100),
    @MaxClients INT = NULL,
    @AutoApproval BIT = 1,
    @Id INT OUTPUT,
    @RefId UNIQUEIDENTIFIER OUTPUT,
    @Pin NVARCHAR(10) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    -- Generate a random 6-character alphanumeric PIN
    SET @Pin = UPPER(LEFT(REPLACE(NEWID(), '-', ''), 6));

    -- Ensure uniqueness (simple retry logic or just assume NEWID entropy is enough for 6 chars for now)
    -- In production, a more robust generator would be used.

    INSERT INTO dbo.RegistrationPolicies (
        Name,
        MaxClients,
        AutoApproval,
        Pin
    )
    VALUES (
        @Name,
        @MaxClients,
        @AutoApproval,
        @Pin
    );

    SELECT 
        @Id = Id,
        @RefId = RefId
    FROM dbo.RegistrationPolicies
    WHERE Id = SCOPE_IDENTITY();
END
