# Project Overview

This project, named WBSKT, is a .NET Core application that provides a client registration and authentication system. It's built with .NET 8 and uses a SQL Server database for data storage. The architecture follows a standard client-server model, where clients register and authenticate with a core service.

The application currently supports user authentication with JWTs and refresh tokens, and provides a REST API for managing registration policies. The client registration workflow is also implemented, allowing clients to register themselves using a policy and receive a client-specific JWT.

## Architecture

The solution is divided into several projects:

*   **Wbskt.Core.Service**: The main web service project, built with ASP.NET Core. It exposes the REST API for all user and client interactions.
*   **Wbskt.Common**: A shared library containing common components such as contracts, services, readers, writers, records, and exceptions.
*   **Wbskt.Database**: A SQL Server database project containing the schema definitions (tables and stored procedures) for the application.
*   **Wbskt.EventBus**: A class library project that contains the core components of the in-process event bus, which is used for decoupling components.
*   **Wbskt.Core.Installer**: A WiX installer project for creating a Windows installer for the application.

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
- `Pin`: `VARCHAR(6)` (unique, server-generated)

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

## Development Conventions

*   **Coding Style**: The project follows standard C# coding conventions.
*   **Dependency Injection**: The project uses the built-in dependency injection container in ASP.NET Core. Services are registered in `Program.cs` and in the `DependencyInjection` class in the `Wbskt.Common` project.
*   **Authentication**: The service uses JWT Bearer authentication with refresh tokens. There are separate authentication schemes for users, clients, and socket servers, each with its own signing key.
*   **API Security**:
    - The API uses `RefId` (GUID) instead of integer IDs to expose resources.
    - A `CurrentUser` service is used to securely access the current user's information from the request context.
*   **Logging**: The project uses Serilog for logging.
*   **Database Access**: The project uses `Microsoft.Data.SqlClient` for database access. All database operations are performed through stored procedures.
*   **Caching**: The project uses a hybrid caching strategy.
    - User-specific data (like policies) is cached per user.
    - Global data (like servers) is cached using a `LastModified` timestamp.
*   **Event Bus**: An in-process event bus is implemented to decouple components. This is currently used for cache invalidation. When a policy is created, updated, or deleted, the `RegistrationPoliciesWriter` publishes an event, and the `PolicyCacheHandler` subscribes to these events to invalidate the cache.
*   **Exception Handling**: The project uses custom exception classes defined in `WbsktExceptions.cs` to handle specific error scenarios.
*   **Testing**: There are no tests in the project currently.

## Future Architecture & Vision: The Workflow Engine

The next major evolution for WBSKT is to build a powerful workflow automation engine on top of the existing foundation. This will transform the project from a client management system into a full-fledged automation platform.

### Target Architecture

The planned architecture will follow a decoupled, microservices-oriented approach to ensure scalability and separation of concerns:

1.  **`Wbskt.Identity.Service`**: The existing service, focused on managing users, authentication, and the *definitions* of clients, policies, and workflows.
2.  **`Wbskt.Socket.Service`**: A new, dedicated service for managing all persistent, real-time WebSocket connections with clients.
3.  **`Wbskt.Workflow.Service`**: A new, headless service that acts as the "brain," listening for triggers and executing workflow logic.

These services will communicate via a message broker (like RabbitMQ) to remain independent and scalable.

### Core Concepts

-   **Workflow**: A user-defined sequence of steps starting with a **Trigger** and performing one or more **Actions**.
-   **Trigger**: The event that starts a workflow. Examples include:
    -   *Real-time Client Data*: `IF client_A.sensor.temperature > 40`.
    -   *Timed Schedule*: A CRON job (e.g., "every day at 5 PM").
    -   *Webhook*: An incoming HTTP request from an external service.
-   **Action**: The work to be done. Examples include:
    -   *Send Payload to Client*: Command a device to perform an action.
    -   *Send Email/SMS*: Send a notification.
    -   *Make HTTP Request*: Call a third-party API.
