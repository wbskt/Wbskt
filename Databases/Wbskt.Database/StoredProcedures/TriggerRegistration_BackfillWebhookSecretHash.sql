-- Gives each registration that has a webhook secret but no hash the hash WebhookSecretHash.Of writes:
-- lower-case hex SHA-256 of the secret's UTF-8 bytes. Run by the post-deployment script for the rows
-- an engine on the previous image wrote, the one write per row the dispatcher would otherwise repeat on
-- every event. A no-op once every row has one.
--
-- The secret is converted through a UTF-8 collation before hashing because HASHBYTES over an NVARCHAR
-- hashes UTF-16LE bytes and C# hashes Encoding.UTF8.GetBytes. Unlike a client secret, an author types
-- this one, so it is not necessarily ASCII and a plain VARCHAR conversion would mangle it.
-- WebhookSecretHashingIntegrationTests pins the two against each other.
CREATE PROCEDURE dbo.TriggerRegistration_BackfillWebhookSecretHash
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.TriggerRegistrations
        SET WebhookSecretHash = LOWER(CONVERT(VARCHAR(64), HASHBYTES('SHA2_256', CONVERT(VARCHAR(800), WebhookSecret COLLATE Latin1_General_100_BIN2_UTF8)), 2))
        WHERE WebhookSecretHash IS NULL
            AND WebhookSecret IS NOT NULL
            AND WebhookSecret <> N'';
END;
GO
