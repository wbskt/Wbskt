# Project Overview

This project, named `Wbskt.Auth.Api`, is a .NET 8 application that provides a centralized authentication and authorization service using OpenIddict. It is designed to be part of a larger microservices architecture.

## Architecture

The service is built with ASP.NET Core and uses the following key technologies:

*   **.NET 8**: The underlying framework.
*   **OpenIddict**: A framework for creating OpenID Connect and OAuth 2.0 servers.
*   **Entity Framework Core**: For database access.
*   **SQL Server**: The database provider.
*   **Serilog**: For logging.
*   **ASP.NET Core Identity**: For user management.

The project is structured as follows:

*   **`Wbskt.Auth.Api.csproj`**: The main project file, containing dependencies and project configuration.
*   **`Program.cs`**: The application's entry point, where services are configured and the application is built.
*   **`Worker.cs`**: A background service that seeds the database with initial data, including roles, permissions, users, and OpenIddict applications.
*   **`Controllers`**: Contains the API controllers for user registration and authentication.
    *   **`AccountController`**: Handles user registration.
    *   **`AuthorizationController`**: Implements the OpenID Connect and OAuth 2.0 flows.
*   **`Data`**: Contains the `AuthDbContext` for interacting with the database.
*   **`Models`**: Contains the data models for users, roles, and permissions.
*   **`appsettings.json`**: Contains application settings, including the database connection string and logging configuration.

## Building and Running

### Prerequisites

*   .NET 8 SDK
*   SQL Server

### Configuration

1.  **Database Connection**: The connection string in `appsettings.json` is configured to connect to a local SQL Server instance. Make sure the database `Wbskt.Database.Auth` exists and the credentials are correct.
2.  **Serilog**: Logging is configured in `appsettings.json` and in a `serilog.json` file that is linked from a `Config` folder outside of the project.

### Running the Application

1.  **Restore Dependencies**:
    ```bash
    dotnet restore
    ```
2.  **Run the Application**:
    ```bash
    dotnet run
    ```
The application will be available at `http://localhost:5010`.

## API Endpoints

### Account

*   `POST /api/account/register`: Registers a new user.

### OpenIddict

*   `GET /connect/authorize`: The OpenID Connect authorization endpoint.
*   `POST /connect/token`: The OpenID Connect token endpoint.
*   `POST /connect/logout`: The OpenID Connect logout endpoint.

## Development Conventions

*   **Dependency Injection**: The project uses the built-in dependency injection container in ASP.NET Core.
*   **Database Seeding**: The `Worker.cs` class is used to seed the database with initial data for development and testing.
*   **Authentication**: The service uses OpenIddict for authentication and authorization.
*   **Logging**: The project uses Serilog for structured logging.
*   **Database Access**: The project uses Entity Framework Core for database access.
