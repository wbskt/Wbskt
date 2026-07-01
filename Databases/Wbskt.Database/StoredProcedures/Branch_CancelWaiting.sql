CREATE PROCEDURE dbo.Branch_CancelWaiting
    @RunId INT
AS
BEGIN
    UPDATE dbo.Branches
    SET
        Status = 'Cancelled',
        UpdatedAt = SYSUTCDATETIME()
    WHERE RunId = @RunId
      AND Status IN ('Waiting', 'WaitingAtJoin');
END;
GO
