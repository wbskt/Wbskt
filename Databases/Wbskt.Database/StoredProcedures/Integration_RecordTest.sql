-- Records the outcome of testing the saved integration, only when what was tested is still what
-- is saved: an edit that landed while the test ran has already cleared the last result, and this
-- one would describe settings that are gone. The rowcount says whether it was kept.
CREATE PROCEDURE dbo.Integration_RecordTest
    @Id                INT,
    @ExpectedUpdatedAt DATETIME2(3),
    @Ok                BIT,
    @Code              NVARCHAR(100) = NULL,
    @Error             NVARCHAR(400) = NULL,
    @Metadata          NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Integrations
    SET LastTestAt    = SYSUTCDATETIME(),
        LastTestOk    = @Ok,
        LastTestCode  = @Code,
        LastTestError = @Error,
        Metadata      = CASE WHEN @Ok = 1 AND @Metadata IS NOT NULL THEN @Metadata ELSE Metadata END
    WHERE Id = @Id
      AND UpdatedAt = @ExpectedUpdatedAt;

    SELECT @@ROWCOUNT;
END
GO
