CREATE PROCEDURE dbo.RegistrationPolicy_Disable
    @WorkspaceId INT,
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.RegistrationPolicies
    SET IsEnabled = 0
    WHERE Id = @Id
      AND WorkspaceId = @WorkspaceId;

    IF @@ROWCOUNT = 0
    BEGIN
        -- Throwing a generic error if the policy wasn't found in the context of the workspace
        THROW 50000, 'Registration policy not found or access denied.', 1;
    END
END
GO
