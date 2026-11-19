-- A partial edit: a NULL parameter keeps the stored value. @RemoveMaxClients = 1 clears the limit,
-- since a NULL @MaxClients means "leave it".
CREATE PROCEDURE dbo.RegistrationPolicy_Update
    @WorkspaceId INT,
    @Id INT,
    @Name NVARCHAR(100) = NULL,
    @MaxClients INT = NULL,
    @RemoveMaxClients BIT = 0,
    @AutoApproval BIT = NULL,
    @IsEnabled BIT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.RegistrationPolicies
    SET Name = COALESCE(@Name, Name),
        MaxClients = CASE WHEN @RemoveMaxClients = 1 THEN NULL ELSE COALESCE(@MaxClients, MaxClients) END,
        AutoApproval = COALESCE(@AutoApproval, AutoApproval),
        IsEnabled = COALESCE(@IsEnabled, IsEnabled)
    WHERE Id = @Id
      AND WorkspaceId = @WorkspaceId;

    IF @@ROWCOUNT = 0
    BEGIN
        THROW 50000, 'Registration policy not found or access denied.', 1;
    END
END
GO
