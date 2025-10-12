# Project Overview

This project, named WBSKT, is a .NET Core application that provides a client registration and authentication system. It's built with .NET 8 and uses a SQL Server database for data storage. The architecture follows a standard client-server model, where clients register and authenticate with a core service.

The system supports different registration policies, including policies with a limited number of clients and time-based policies. Clients can register manually or automatically, and upon successful registration, they receive an authentication token for accessing other resources.

The solution is divided into several projects:

*   **Wbskt.Core.Service**: The main web service project, built with ASP.NET Core. It exposes a REST API for user registration and login.
*   **Wbskt.Common**: A shared library containing common contracts, services, and utilities used by other projects in the solution.
*   **Wbskt.Database**: A SQL Server database project containing the schema definitions for the application's database.
*   **Wbskt.Core.Installer**: A WiX installer project for creating a Windows installer for the application.

## Building and Running

To build and run this project, you will need the .NET 8 SDK and a SQL Server instance.

1.  **Database Setup**:
    *   Create a database named `Wbskt.Core`.
    *   Execute the SQL scripts in the `Wbskt.Database` project to create the necessary tables and stored procedures. The scripts are located in the `Tables` and `Stored Procedures` folders.

2.  **Configuration**:
    *   Update the connection string in `appsettings.json` in the `Wbskt.Core.Service` project to point to your SQL Server instance.

3.  **Running the Service**:
    *   You can run the service from Visual Studio by setting `Wbskt.Core.Service` as the startup project and pressing F5.
    *   Alternatively, you can use the `dotnet run` command in the `Wbskt.Core.Service` directory:

    ```bash
    dotnet run --project Wbskt.Core.Service/Wbskt.Core.Service.csproj
    ```

## Development Conventions

*   **Coding Style**: The project follows standard C# coding conventions.
*   **Dependency Injection**: The project uses the built-in dependency injection container in ASP.NET Core. Services are registered in `Program.cs` and in the `DependencyInjection` class in the `Wbskt.Common` project.
*   **Authentication**: The service uses JWT Bearer authentication.
*   **Logging**: The project uses Serilog for logging.
*   **Database Access**: The project uses `Microsoft.Data.SqlClient` for database access. Stored procedures are used for database operations.
*   **Testing**: There are no tests in the project currently.
