# Project Overview

This project, named WBSKT, is a .NET Core application that provides a distributed client registration, management, and workflow automation system. It's built with .NET 10 (latest) and uses a microservice-oriented architecture.

## Architecture

The solution is divided into several host services:

*   **Wbskt.Auth.Host**: Handles user registration, authentication (JWT), and workspace management.
*   **Wbskt.Management.Host**: Provides the management API for registration policies, clients, and workflow definitions.
*   **Wbskt.Socket.Host**: Manages persistent WebSocket connections with registered clients.
*   **Wbskt.Workflow.Engine.Host**: The "brain" of the system, executing workflows triggered by client events or schedules.

### Communication
- **REST APIs**: Used for management and authentication.
- **WebSockets**: Used for real-time bidirectional communication with clients.
- **RabbitMQ**: Used for inter-service communication via a distributed event bus (MassTransit).

## Key Components

- **Registration Policies**: Define how clients can join a workspace (PIN-based, auto-approval).
- **Workflows**: User-defined automation logic (Nodes & Edges) triggered by client payloads or property changes.
- **Clients**: Edge devices or applications that connect via the SDK and exchange telemetry/commands.

## Development Tools

### WBSKT Control Dashboard
A comprehensive HTML/JS tool located at `Wbskt.Dashboard.html` in the root directory. It allows:
- User login and workspace selection.
- Management of Policies and Workflows.
- Real-time client simulation (multi-client support).
- Monitoring client logs and telemetry.

### Port Configuration (Development)
- **Auth**: `https://localhost:7000`
- **Management**: `https://localhost:7010`
- **Socket**: `https://localhost:7020`
- **Workflow Engine**: `https://localhost:7030`

## Development Conventions

*   **RefId usage**: The API exposes GUID-based `RefId`s instead of internal integer IDs for all resources.
*   **Event-Driven**: Most actions (like client payloads or state changes) publish events to RabbitMQ to trigger downstream logic.
*   **Reference Mappers**: Used to securely map between external `RefId`s and internal database IDs within each service's context.

## Future Architecture & Vision: The Workflow Engine

The next major evolution for WBSKT is to build a powerful workflow automation engine on top of the existing foundation. This will transform the project from a client management system into a full-fledged automation platform.

### Target Architecture

The planned architecture will follow a decoupled, microservices-oriented approach to ensure scalability and separation of concerns:

1.  **`Wbskt.Management.Service`**: The existing service, focused on managing users, authentication, and the *definitions* of clients, policies, and workflows.
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
