CREATE PROCEDURE dbo.Run_SetOutput
    @RunId      INT,
    @OutputJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    -- Written by a control:end node that declares an output, before its branch completes, so the
    -- finalizer that runs when the last branch ends reads it back with the run. Only while the run is
    -- still going: a terminal run's result has already been delivered.
    UPDATE dbo.Runs WITH (ROWLOCK)
    SET OutputJson = @OutputJson
    WHERE Id = @RunId
      AND Status IN (N'Running', N'Failing', N'Cancelling');
END;
GO
