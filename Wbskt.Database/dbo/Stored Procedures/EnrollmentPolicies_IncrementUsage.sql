/*
    Procedure: dbo.EnrollmentPolicies_IncrementUsage
    Purpose: Increments the CurrentUsage for a policy and deactivates it if the max is reached.
    Parameters:
        - @Id INT: The enrollment policy Id to increment
    Returns: None (updates CurrentUsage and IsActive fields)
    Author: Richard Joy
    Date: 2025-04-25
    Last Modified: 2025-04-25 by Richard Joy - Initial version
*/
CREATE PROCEDURE dbo.EnrollmentPolicies_IncrementUsage
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @PolicyType INT;
    DECLARE @MaxClients INT;
    DECLARE @CurrentUsage INT;
    
    -- Get current policy details
    SELECT @PolicyType = PolicyType, @MaxClients = MaxClients, @CurrentUsage = CurrentUsage
    FROM dbo.EnrollmentPolicies
    WHERE Id = @Id;
    
    IF @@ROWCOUNT = 0
    BEGIN
        RAISERROR ('Enrollment policy with ID %d not found', 16, 1, @Id);
        RETURN;
    END
    
    -- Increment usage
    UPDATE dbo.EnrollmentPolicies
    SET CurrentUsage = CurrentUsage + 1,
        LastModified = CURRENT_TIMESTAMP
    WHERE Id = @Id;
    
    -- Check if policy should be deactivated
    IF @PolicyType IN (2, 4) AND @MaxClients IS NOT NULL
    BEGIN
        IF (@CurrentUsage + 1) >= @MaxClients
        BEGIN
            UPDATE dbo.EnrollmentPolicies
            SET IsActive = 0,
                LastModified = CURRENT_TIMESTAMP
            WHERE Id = @Id;
        END
    END
END; 