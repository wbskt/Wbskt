CREATE PROCEDURE dbo.RegistrationPolicy_Create
    @WorkspaceId INT,
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
    SET @RefId = NEWID();

    INSERT INTO dbo.RegistrationPolicies (
        WorkspaceId,
        RefId,
        Name,
        MaxClients,
        AutoApproval,
        Pin
    )
    VALUES (
        @WorkspaceId,
        @RefId,
        @Name,
        @MaxClients,
        @AutoApproval,
        @Pin
    );

    SET @Id = SCOPE_IDENTITY();
END
GO
