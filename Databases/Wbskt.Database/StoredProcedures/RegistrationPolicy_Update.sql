CREATE PROCEDURE dbo.RegistrationPolicy_Update
    @WorkspaceId INT,
    @Id INT,
    @Name NVARCHAR(100),
    @AutoApproval BIT,
    @IsEnabled BIT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.RegistrationPolicies
    SET Name = @Name,
        AutoApproval = @AutoApproval,
        IsEnabled = @IsEnabled
    WHERE Id = @Id
      AND WorkspaceId = @WorkspaceId;

    IF @@ROWCOUNT = 0
    BEGIN
        THROW 50000, 'Registration policy not found or access denied.', 1;
    END
END
GO
