# WBSKT

This document provides an overview of the WBSKT project, its architecture, and its current status.

## Current Status

- **User Authentication**: The application supports user registration and login using JWTs and refresh tokens.
- **Policy Management**: Users can create, read, update, and delete registration policies via a REST API.
- **API Security**:
    - The API uses `RefId` (GUID) instead of integer IDs to expose resources, enhancing security.
    - A `CurrentUser` service is used to securely access the current user's information from the request context.
- **Caching**:
    - A hybrid caching strategy is in place.
    - User-specific data (like policies) is cached per user.
    - Global data (like servers) is cached using a `LastModified` timestamp.
- **Event Bus**: An in-process event bus is implemented to decouple components. This is currently used for cache invalidation.

## Client Registration Workflow

The client registration flow is designed to be secure and flexible, allowing users to control how clients can register with the system. Here is a step-by-step breakdown of the process:

1.  **Prerequisites**: Before a client can register, a user must have already created a **Registration Policy** through the `/api/policies` endpoint. This policy defines the rules for registration, such as the PIN, the maximum number of clients, and an optional expiration date. The user will then provide the `PolicyRefId` and the `Pin` to the client application.

2.  **Client Initiates Registration**: The client application makes a `POST` request to the `/api/registrations` endpoint with the `PolicyRefId`, `Pin`, and an optional `ClientName` in the request body.

3.  **Policy Validation**: The `RegistrationService` receives the request and performs the following validation checks:
    *   It retrieves the policy from the database using the `PolicyRefId`.
    *   It verifies that the provided `Pin` matches the one stored in the policy.
    *   It checks if the policy has expired.

4.  **Client Limit Enforcement**: If the policy has a `MaxClients` limit, the service checks how many clients are already registered with this policy. If the limit has been reached, the registration is denied.

5.  **Client Creation**: If all validation checks pass, the service creates a new client record in the database. This new client is associated with the user who owns the policy.

6.  **Socket Server Discovery**: The service then queries the database to find an available socket server. For now, it selects the first available server, but a proper load balancing strategy will be implemented in the future.

7.  **Client Token Generation**: The service generates a new, client-specific JWT. This token is signed with a different key than the user's JWT and contains the following claims:
    *   `ClientId`: The integer ID of the new client.
    *   `ClientUniqueId`: The `RefId` (GUID) of the new client.
    *   `SocketServer`: The address of the socket server that the client should connect to.

8.  **Response**: The service returns the client JWT (`AuthToken`) and the `SocketServerAddress` to the client.

9.  **Client Connects to Socket Server**: The client application then uses the `AuthToken` and the `SocketServerAddress` to establish a persistent connection with the socket server.

## TODO

- **Complete Client Registration**: The `RegistrationService` needs to be fully implemented to allow clients to register themselves using the policies created by users.
- **Granular Cache Invalidation**: The cache invalidation for policies currently clears the entire user-specific cache. This could be improved to only invalidate the specific policy that was changed.
- **Socket Server Load Balancing**: The `RegistrationService` currently just picks the first available socket server. A proper load balancing strategy should be implemented.
- **Testing**: There are currently no tests in the project. Unit tests and integration tests should be added to ensure the quality and stability of the application.
- **Logging**: Review and improve logging throughout the application to ensure that all important events are logged.
- **Error Handling**: Review and improve error handling and exception messages to provide more meaningful feedback to the client.

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

### Registrations

- `POST /api/registrations`: Registers a new client using a registration policy.

## Database Schema

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

### Servers

- `Id`: `INT`
- `PublicDomainName`: `VARCHAR(256)`
- `Status`: `INT`

## Project Vision: A Workflow Automation Engine

The core of WBSKT is a powerful workflow automation engine that connects triggers to actions. This allows users to create automated sequences that react to events from various sources, including real-time data from connected clients.

A **Workflow** is a sequence of steps that starts with a single **Trigger** and executes one or more **Actions**, potentially controlled by **Modifiers** (like if-conditions).

- **Trigger**: What starts the workflow (e.g., a webhook call, a timed schedule, or a specific data pattern from a client).
- **Action**: A unit of work to be done (e.g., send a command to a client, send an email).
- **Execution Context**: When a trigger fires, it creates a "context" (containing data from the trigger) that flows through the workflow, can be modified, and is used by actions.

## Trigger Types

Triggers can be categorized by what initiates them:

#### 1. User-Initiated Triggers
*   **Manual Trigger**: A user clicks a button in a UI or makes a direct API call to run a workflow. Essential for testing and direct control.

#### 2. Scheduled Triggers
*   **Timed (CRON) Trigger**: Runs a workflow on a recurring schedule (e.g., every day at midnight, every 5 minutes).
*   **Delayed Trigger**: Runs a workflow once, a specific amount of time after it has been scheduled. (e.g., "run this in 2 hours").

#### 3. Webhook & External Event Triggers
*   **Generic Webhook**: A public URL that starts a workflow when it receives an HTTP request. This is the key to integrating with countless third-party services (e.g., GitHub, Stripe, Shopify).
*   **Email Received Trigger**: Starts a workflow when an email is sent to a specific, unique email address. This would require integrating with an email parsing service like Mailgun or SendGrid Inbound Parse.

#### 4. Real-time Client Data Triggers
This is where you can listen to the stream of data from your connected clients and trigger workflows based on specific conditions.

*   **Client Data Match Trigger**: The user defines a condition against the data packets being received from a specific client (or group of clients). The workflow engine continuously evaluates the incoming data stream.
    *   *Example*: Trigger when `client_A.sensor_data.temperature > 40`.
*   **Client Data Sequence Trigger**: Triggers when a specific sequence of events happens from a client within a certain timeframe.
    *   *Example*: Trigger if `client_C.door_sensor` sends `{"status": "open"}` followed by `{"status": "closed"}` within 5 seconds.
*   **Client Connectivity Trigger**: Triggers based on the client's connection status to the socket server.
    *   *Example*: Trigger when `client_D` disconnects unexpectedly.

#### 5. Internal Application Triggers
*   **New User Registration**: Triggers a workflow when a new user signs up for your service (e.g., to send a welcome email).
*   **New Client Registration**: Triggers a workflow when a new client is successfully registered via a policy (e.g., to notify the owner).

## Action Types

Actions are the "work" part of the workflow.

#### 1. Internal & Client-Facing Actions
*   **Send Payload to Client**: Send a specific JSON payload to one or more connected clients.
*   **Update Client Configuration**: Send a special payload that instructs a client to update its internal settings.
*   **Request Data from Client**: Send a message to a client asking it to report its current state.
*   **Create/Update/Delete Policy**: Programmatically manage registration policies from within a workflow.
*   **Log Data**: Write data from the workflow context to a dedicated log file or database table.

#### 2. Notification & Communication Actions
*   **Send Email**: Send an email via an integrated service (SendGrid, Mailgun, AWS SES).
*   **Send SMS**: Send a text message via a service like Twilio.
*   **Send WhatsApp Message**: Integrate with the WhatsApp Business API.
*   **Send Telegram Message**: Use the Telegram Bot API to send messages.
*   **Send Slack/Discord Message**: Post a message to a specific channel.

#### 3. External Integration Actions
*   **Make HTTP Request**: A generic action to call any external API.
*   **Write to Google Sheets**: Append a new row to a specified Google Sheet.
*   **Create Trello Card**: Create a new card on a Trello board.
