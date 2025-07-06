CREATE PROCEDURE dbo.EnrollmentPolicies_CleanupExpired
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Deactivate expired time-limited policies
    UPDATE dbo.EnrollmentPolicies
    SET IsActive = 0,
        LastModified = CURRENT_TIMESTAMP
    WHERE PolicyType = 1  -- TimeLimited
      AND ExpiryDate IS NOT NULL
      AND ExpiryDate <= CURRENT_TIMESTAMP
      AND IsActive = 1;
END; 