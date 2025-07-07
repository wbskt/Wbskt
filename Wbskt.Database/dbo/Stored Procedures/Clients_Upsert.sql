/* ---------------------------------------------------------------- */
/* Clients_Upsert                                         */
/* Author: Richard Joy                                              */
/* Updated by: Richard Joy                                          */
/* Create date: 25-Apr-2025                                         */
/* Description: Insert or update a client based on ClientUniqueId   */
/* ---------------------------------------------------------------- */
CREATE PROCEDURE dbo.Clients_Upsert
    @Id         INT                 OUTPUT,
    @Name       VARCHAR(100),
    @UniqueRef  UNIQUEIDENTIFIER,
    @UserId     INT,
    @ServerId   INT,
    @PolicyId   INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @ExistingId INT;

    SELECT @ExistingId = Id
    FROM dbo.Clients
    WHERE UniqueRef = @UniqueRef;

    IF @ExistingId IS NOT NULL
        BEGIN
            -- Update existing client; only name, server, and policy can be updated
            UPDATE dbo.Clients
            SET Name        = @Name,
                ServerId    = @ServerId,
                PolicyId    = @PolicyId
            WHERE
                Id          = @ExistingId;

            SET @Id = @ExistingId;
        END
    ELSE
        BEGIN
            -- Insert new client
            INSERT INTO dbo.Clients
            ( Name
            , ServerId
            , UniqueRef
            , UserId
            , PolicyId)
            VALUES
                ( @Name
                , @ServerId
                , @UniqueRef
                , @UserId
                , @PolicyId);

            SET @Id = SCOPE_IDENTITY();
        END
END;
