# WBSKT

This document provides an overview of the WBSKT project, its architecture, and its current status.

## Current Status

- **User Authentication**: The application supports user registration and login using JWTs and refresh tokens.
- **Policy Management**: Users can create, read, update, and delete registration policies via a REST API.
- **API Security**: The API uses `RefId` (GUID) instead of integer IDs to expose resources, enhancing security.

## Next Steps

The next step is to implement the client registration functionality, which will allow clients to register themselves using the policies created by users.

## API Endpoints

### Users

- `POST /api/users/register`: Registers a new user.
- `POST /api/users/login`: Logs in a user and returns an access token and a refresh token.
- `POST /api/users/refresh-token`: Refreshes an access token using a refresh token.

### Policies

- `POST /api/policies`: Creates a new registration policy.
- `GET /api/policies`: Gets all registration policies for the authenticated user.
- `GET /api/policies/{refId}`: Gets a specific registration policy by its `RefId`.
- `PUT /api/policies/{refId}`: Updates a registration policy.
- `DELETE /api/policies/{refId}`: Deletes a registration policy.

## Database

### Users

- `Id`: `INT`
- `Name`: `NVARCHAR(100)`
- `EmailId`: `NVARCHAR(100)`
- `PasswordHash`: `VARCHAR(512)`

### UserRefreshTokens

- `Id`: `INT`
- `UserId`: `INT`
- `Token`: `VARCHAR(256)`
- `Expires`: `DATETIME`
- `Created`: `DATETIME`
- `CreatedByIp`: `VARCHAR(50)`
- `Revoked`: `DATETIME`
- `RevokedByIp`: `VARCHAR(50)`
- `ReplacedByToken`: `VARCHAR(256)`

### RegistrationPolicies

- `Id`: `INT`
- `RefId`: `GUID`
- `UserId`: `INT`
- `Name`: `NVARCHAR(100)`
- `MaxClients`: `INT` (nullable)
- `Expiry`: `DATETIME` (nullable)
- `Pin`: `INT`

### Clients

- `Id`: `INT`
- `RefId`: `GUID`
- `UserId`: `INT`
- `RegistrationPolicyId`: `INT`
- `Name`: `NVARCHAR(100)` (nullable)
- `Active`: `BOOL`