-- A partial edit: a NULL parameter keeps the stored value. @RemoveMaxClients = 1 clears the limit,
-- since a NULL @MaxClients means "leave it". A new limit below the clients already registered changes
-- nothing and returns that count in @RegisteredOverLimit (NULL when the edit went through). It is
-- checked under the same update lock on the policy row that Client_Create and Client_UpdateStatuses
-- take, so an approval cannot slip in between.
CREATE PROCEDURE dbo.RegistrationPolicy_Update
    @WorkspaceId INT,
    @Id INT,
    @Name NVARCHAR(100) = NULL,
    @MaxClients INT = NULL,
    @RemoveMaxClients BIT = 0,
    @AutoApproval BIT = NULL,
    @IsEnabled BIT = NULL,
    @RegisteredOverLimit INT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    IF NOT EXISTS (SELECT 1 FROM dbo.RegistrationPolicies WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id AND WorkspaceId = @WorkspaceId)
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50000, 'Registration policy not found or access denied.', 1;
    END

    IF @MaxClients IS NOT NULL AND @RemoveMaxClients = 0
    BEGIN
        DECLARE @Registered INT = (SELECT COUNT(*) FROM dbo.Clients WHERE PolicyId = @Id AND Status = 1);
        IF @Registered > @MaxClients
        BEGIN
            ROLLBACK TRANSACTION;
            SET @RegisteredOverLimit = @Registered;
            RETURN;
        END
    END

    UPDATE dbo.RegistrationPolicies
    SET Name = COALESCE(@Name, Name),
        MaxClients = CASE WHEN @RemoveMaxClients = 1 THEN NULL ELSE COALESCE(@MaxClients, MaxClients) END,
        AutoApproval = COALESCE(@AutoApproval, AutoApproval),
        IsEnabled = COALESCE(@IsEnabled, IsEnabled)
    WHERE Id = @Id
      AND WorkspaceId = @WorkspaceId;

    COMMIT TRANSACTION;
END
GO
