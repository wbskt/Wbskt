/*
    Procedure: dbo.EnrollmentPolicies_CleanupExpired
    Purpose: Deactivates expired time-limited enrollment policies.
    Parameters: None
    Returns: None (updates IsActive and LastModified fields)
    Author: Richard Joy
    Date: 2025-04-25
    Last Modified: 2025-04-25 by Richard Joy - Initial version
*/
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