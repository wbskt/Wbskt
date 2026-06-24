CREATE PROCEDURE dbo.Run_TransitionStatus
    @RunId INT,
    @FromStatus NVARCHAR(32),
    @ToStatus NVARCHAR(32)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Runs WITH (ROWLOCK)
    SET Status = @ToStatus
    WHERE Id = @RunId
      AND Status = @FromStatus;

    SELECT @@ROWCOUNT;
END;
GO
