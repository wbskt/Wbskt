CREATE PROCEDURE dbo.Run_TransitionStatus
    @RunId INT,
    @FromStatus NVARCHAR(32),
    @ToStatus NVARCHAR(32),
    @CancellationRequestedAt DATETIME2(3) = NULL,
    @CancellationReason NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Single status write (2.3): callers that are also requesting cancellation pass the
    -- reason/timestamp here instead of issuing a follow-up UpdateStatusAsync call.
    UPDATE dbo.Runs WITH (ROWLOCK)
    SET Status = @ToStatus,
        CancellationRequestedAt = COALESCE(@CancellationRequestedAt, CancellationRequestedAt),
        CancellationReason = COALESCE(@CancellationReason, CancellationReason)
    WHERE Id = @RunId
      AND Status = @FromStatus;

    SELECT @@ROWCOUNT;
END;
GO
