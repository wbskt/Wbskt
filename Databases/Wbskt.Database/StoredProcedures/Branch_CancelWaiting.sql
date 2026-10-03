CREATE PROCEDURE dbo.Branch_CancelWaiting
    @RunId INT
AS
BEGIN
    -- No SET NOCOUNT ON: the caller returns the rows-affected count from ExecuteNonQuery.
    UPDATE dbo.Branches
    SET
        Status = 'Cancelled',
        UpdatedAt = SYSUTCDATETIME()
    WHERE RunId = @RunId
      AND Status IN ('Waiting', 'WaitingAtJoin');
END;
GO
