-- The staff audit row a retried admin call already wrote, if any.
CREATE PROCEDURE dbo.StaffAudit_FindBy_IdempotencyKey
    @IdempotencyKey UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Id,
        Action,
        TargetRefId,
        SubjectRefId,
        Outcome,
        Detail,
        BodyHash
    FROM dbo.StaffAudit
    WHERE IdempotencyKey = @IdempotencyKey;
END
