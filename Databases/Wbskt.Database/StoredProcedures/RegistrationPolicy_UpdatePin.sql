-- Replaces a policy's PIN, so a leaked one stops registering devices. Devices already registered
-- are unaffected: the PIN is only used to register. @Pin comes from the caller (RegistrationPins);
-- a clash fails on UQ_RegistrationPolicies_Pin and the caller draws again, as for a new policy.
CREATE PROCEDURE dbo.RegistrationPolicy_UpdatePin
    @WorkspaceId INT,
    @Id INT,
    @Pin NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.RegistrationPolicies
    SET Pin = @Pin
    WHERE Id = @Id
      AND WorkspaceId = @WorkspaceId;

    IF @@ROWCOUNT = 0
    BEGIN
        THROW 50000, 'Registration policy not found or access denied.', 1;
    END
END
GO
