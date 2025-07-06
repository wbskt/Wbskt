/* -------------------------------- */
/* EnrollmentPolicies_Insert       */
/* Author: Richard Joy              */
/* Updated by: Richard Joy          */
/* Create date: 25-Apr-2025         */
/* Description: Insert new enrollment policy with provided GUID */
/* -------------------------------- */
CREATE PROCEDURE dbo.EnrollmentPolicies_Insert
    @Id           INT             OUTPUT,
    @UserId       INT,
    @PolicyRef    UNIQUEIDENTIFIER,
    @Name         VARCHAR(100),
    @PolicyType   INT,
    @MaxClients   INT             = NULL,
    @ExpiryDate   DATETIME        = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.EnrollmentPolicies
    ( UserId
    , PolicyRef
    , Name
    , PolicyType
    , MaxClients
    , ExpiryDate
    , CurrentUsage
    , IsActive
    )
    VALUES
        ( @UserId
        , @PolicyRef
        , @Name
        , @PolicyType
        , @MaxClients
        , @ExpiryDate
        , 0
        , 1
        );

    SELECT @Id = SCOPE_IDENTITY();
END; 