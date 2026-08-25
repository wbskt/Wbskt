-- Replaces Client_Verify, which matched the presented secret in SQL:
--     WHERE c.RefId = @RefId AND c.Secret = @Secret
-- Three problems with that, all fixed by moving the comparison out of the database. The secret was
-- stored in plaintext, so any read of this table - or of a backup - impersonated every device in
-- every fleet. The default collation is case-insensitive, so a secret matched regardless of case
-- and lost roughly a bit of entropy per alphabetic character. And `=` is not a constant-time
-- comparison.
--
-- This looks the client up by reference alone and returns the stored hash. The caller compares in
-- C# with CryptographicOperations.FixedTimeEquals; see ClientSecrets.
CREATE PROCEDURE dbo.Client_GetCredentialBy_RefId
    @RefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        c.Id,
        c.RefId,
        c.WorkspaceId,
        c.PolicyId,
        p.RefId AS PolicyRefId,
        c.Name,
        c.SecretHash,
        c.Status,
        c.IsConnected,
        c.ConnectedAt,
        c.LastActivityAt,
        c.LastRttMs,
        c.RttMeasuredAt,
        c.CreatedAt
    FROM dbo.Clients c
    INNER JOIN dbo.RegistrationPolicies p ON c.PolicyId = p.Id
    WHERE c.RefId = @RefId;
END
GO
