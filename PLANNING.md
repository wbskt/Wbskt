# **WBSKT Phase 1 — The Core Engine & Foundation**

## Goal

Create the foundational services and data structures for defining, storing, and manually executing a simple workflow.

---

## **Action Item 1: Solution & Project Restructuring**

This step reorganizes the solution to logically separate different parts of the system, preparing for a microservices architecture.

### **1.1. Rename Existing Service**

* Rename the project folder:

    * From: `Wbskt.Core.Service`
    * To: `Wbskt.Management.Service`
* Open the solution file (`.sln`) in a text editor and update the project reference:

  ```plaintext
  Wbskt.Management.Service\Wbskt.Management.Service.csproj
  ```
* This service now explicitly handles **Management and management**.

### **1.2. Create New Projects**

* Create two new “ASP.NET Core Web API” projects:

    * `Wbskt.Socket.Service`
    * `Wbskt.Workflow.Service`
* These will be mostly empty for now but establish the structure.

### **1.3. Organize the Solution**

* In your IDE:

    * Create a **Solution Folder** named `services`.
    * Move the following projects into it:

        * `Wbskt.Management.Service`
        * `Wbskt.Socket.Service`
        * `Wbskt.Workflow.Service`

---

## **Action Item 2: Database Schema (`Wbskt.Database` project)**

Defines how workflows and their components are persisted.

### **2.1. Create `Workflows.sql`**

This table holds the high-level definition of a workflow.

```sql
CREATE TABLE [dbo].[Workflows] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [RefId] UNIQUEIDENTIFIER NOT NULL,
    [UserId] INT NOT NULL,
    [Name] NVARCHAR(100) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    [IsEnabled] BIT NOT NULL DEFAULT 0,
    [TriggerType] VARCHAR(50) NOT NULL, -- e.g., "Manual", "Timed", "Webhook"
    [TriggerConfiguration] NVARCHAR(MAX) NULL, -- Stores JSON data like a CRON schedule
    [LastModified] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [PK_Workflows] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [UNQ_Workflows_RefId] UNIQUE NONCLUSTERED ([RefId] ASC),
    CONSTRAINT [FK_Workflows_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id])
);
```

---

### **2.2. Create `WorkflowSteps.sql`**

Stores the individual steps that make up a single workflow.

```sql
CREATE TABLE [dbo].[WorkflowSteps] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [WorkflowId] INT NOT NULL,
    [StepOrder] INT NOT NULL, -- Defines the execution order for linear flows
    [Name] NVARCHAR(100) NOT NULL, -- User-friendly name for the step
    [StepType] VARCHAR(50) NOT NULL, -- "Action" or "Modifier"
    [StepIdentifier] VARCHAR(100) NOT NULL, -- e.g., "action.log", "modifier.if"
    [StepConfiguration] NVARCHAR(MAX) NULL, -- JSON configuration for this step
    [OnSuccessStepId] INT NULL, -- For branching: ID of the next step on success/true
    [OnFailureStepId] INT NULL, -- For branching: ID of the next step on failure/false
    CONSTRAINT [PK_WorkflowSteps] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_WorkflowSteps_Workflows] FOREIGN KEY ([WorkflowId]) 
        REFERENCES [dbo].[Workflows]([Id]) ON DELETE CASCADE
);
```

> **Note:** The `ON DELETE CASCADE` ensures that deleting a workflow automatically removes its associated steps.

---

### **2.3. Create `WorkflowExecutions.sql`**

Logs every time a workflow runs — useful for auditing and debugging.

```sql
CREATE TABLE [dbo].[WorkflowExecutions] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [WorkflowId] INT NOT NULL,
    [Status] VARCHAR(20) NOT NULL, -- "Pending", "Running", "Success", "Failed"
    [TriggeredAt] DATETIME2 NOT NULL,
    [CompletedAt] DATETIME2 NULL,
    [InitialContext] NVARCHAR(MAX) NULL, -- JSON of the data that started the workflow
    [ErrorLog] NVARCHAR(MAX) NULL, -- Store any exception messages
    CONSTRAINT [PK_WorkflowExecutions] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_WorkflowExecutions_Workflows] FOREIGN KEY ([WorkflowId]) 
        REFERENCES [dbo].[Workflows]([Id]) ON DELETE CASCADE
);
```

---

## **Action Item 3: New Core Engine Project (`Wbskt.Workflow.Service`)**

This project will host the **workflow execution engine**. For Phase 1, only contracts and placeholders are defined.

### **3.1. Define Core Interfaces**

#### `IAction.cs`

```csharp
// Represents the result of an action, indicating success or failure.
public record ActionResult(bool IsSuccess, string? ErrorMessage = null);

// Every action must implement this interface.
public interface IAction
{
    Task<ActionResult> ExecuteAsync(WorkflowContext context, CancellationToken cancellationToken);
}
```

#### `IWorkflowEngine.cs`

```csharp
public interface IWorkflowEngine
{
    Task ExecuteWorkflowAsync(Guid workflowRefId, WorkflowContext initialContext);
}
```

---

### **3.2. Define the Context**

#### `WorkflowContext.cs`

```csharp
// Holds the data that flows through a workflow execution.
public class WorkflowContext
{
    public Guid WorkflowExecutionId { get; }
    public Dictionary<string, object> Properties { get; }

    public WorkflowContext(Guid executionId, Dictionary<string, object> initialProperties)
    {
        WorkflowExecutionId = executionId;
        Properties = initialProperties;
    }
}
```

---

### **3.3. Create Placeholder Implementation**

#### `WorkflowEngine.cs`

```csharp
public class WorkflowEngine : IWorkflowEngine
{
    // Inject dependencies later (e.g., logger, service provider)
    public WorkflowEngine() {}

    public Task ExecuteWorkflowAsync(Guid workflowRefId, WorkflowContext initialContext)
    {
        // In Phase 2, we will implement the logic here.
        // For now, this confirms the service can be called.
        Console.WriteLine($"Pretending to execute workflow {workflowRefId}.");
        return Task.CompletedTask;
    }
}
```

---

## **Action Item 4: Management API (`Wbskt.Management.Service`)**

Provides the API/UI layer for managing workflows.

### **4.1. Create Data Records (`Wbskt.Common`)**

Define record types mirroring the database tables:

* `WorkflowRecord`
* `WorkflowStepRecord`

---

### **4.2. Create Readers/Writers (`Wbskt.Common`)**

Define interfaces and database access classes:

* `IWorkflowsReader`, `IWorkflowsWriter`
* `IWorkflowStepsReader`, `IWorkflowStepsWriter`

Implement corresponding classes like `WorkflowsDatabaseReader` and `WorkflowsDatabaseWriter`.

---

### **4.3. Create `WorkflowsController.cs`**

Expose REST endpoints:

| Method     | Endpoint                 | Description                                                   |
| :--------- | :----------------------- | :------------------------------------------------------------ |
| **GET**    | `/api/workflows`         | List workflows for the authenticated user (`_currentUser.Id`) |
| **POST**   | `/api/workflows`         | Create a new workflow and its steps                           |
| **GET**    | `/api/workflows/{refId}` | Retrieve a workflow and its steps                             |
| **PUT**    | `/api/workflows/{refId}` | Update workflow properties (e.g., name, status)               |
| **DELETE** | `/api/workflows/{refId}` | Delete a workflow                                             |

---

## **End of Phase 1**

By the end of this phase:

* You have a **fully functional management API** for creating and managing workflows.
* The **database** and **service structure** are in place.
* The system is ready for **Phase 2**, where the actual workflow execution logic will be implemented.

---
Here’s your **WBSKT Phase 2** document reformatted into clean, professional **Markdown** — ideal for technical documentation or a project README:

---

# **WBSKT Phase 2 — Trigger Implementation & Basic Execution**

## Goal

Make workflows “live” by implementing the **core execution logic** in the `WorkflowEngine` and introducing the first two types of triggers:

* **Manual Trigger** – for direct control and testing
* **Timed Trigger** – for automated scheduling

---

## **Action Item 1: Implement the Core `WorkflowEngine` (`Wbskt.Workflow.Service`)**

This is the most critical step. You’ll turn the placeholder engine from Phase 1 into a **fully functional executor** that can process a linear sequence of workflow steps.

---

### **1.1. Dependency Injection**

Modify `WorkflowEngine.cs` to inject the necessary services:

```csharp
private readonly IServiceProvider _serviceProvider;
private readonly IWorkflowsReader _workflowsReader;
private readonly IWorkflowExecutionsWriter _executionsWriter;
private readonly ILogger<WorkflowEngine> _logger;

public WorkflowEngine(
    IServiceProvider serviceProvider,
    IWorkflowsReader workflowsReader,
    IWorkflowExecutionsWriter executionsWriter,
    ILogger<WorkflowEngine> logger)
{
    _serviceProvider = serviceProvider;
    _workflowsReader = workflowsReader;
    _executionsWriter = executionsWriter;
    _logger = logger;
}
```

> 💡 **Note:** Injecting `IServiceProvider` as a *service locator* is acceptable here — the engine needs to dynamically resolve different `IAction` implementations at runtime based on database data.

---

### **1.2. Flesh Out `ExecuteWorkflowAsync`**

This method is the **heart of the engine**.

**Step-by-step logic:**

1. **Load Workflow**

    * Use `_workflowsReader` to fetch the `WorkflowRecord` and its associated steps from the database using `workflowRefId`.
    * If not found → log an error and return.

2. **Create Execution Record**

    * Create a new record in `WorkflowExecutions` with status `"Running"`.
    * This provides an audit trail.

3. **Initialize Context**

    * Use the `initialContext` passed from the trigger.

4. **Step Execution Loop**

    * Order steps by `StepOrder`.
    * Start with the first step.
    * In a `try...catch`:

        * Resolve the correct action from its `StepIdentifier` (e.g., `"action.log"`).
        * Create a scoped provider:

          ```csharp
          using var scope = _serviceProvider.CreateScope();
          ```
        * Resolve the `IAction` implementation dynamically (via dictionary or naming convention).
        * Execute the action:

          ```csharp
          var result = await action.ExecuteAsync(context, cancellationToken);
          ```
        * If `IsSuccess == false`, break the loop and jump to failure handling.
        * For now, assume a linear flow (next step by order).

5. **Update Execution Record**

    * If all steps succeed → set status `"Success"`.
    * If any exception → set status `"Failed"` and log the exception.
    * Update the `CompletedAt` timestamp.

---

### **1.3. Create a Simple “Log” Action for Testing**

To validate the engine, create a simple `LogAction` class.

#### `LogAction.cs`

```csharp
public class LogAction : IAction
{
    private readonly ILogger<LogAction> _logger;

    public LogAction(ILogger<LogAction> logger) { _logger = logger; }

    public Task<ActionResult> ExecuteAsync(WorkflowContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing LogAction for WorkflowExecutionId: {Id}", context.WorkflowExecutionId);

        // Log properties for debugging
        foreach (var prop in context.Properties)
        {
            _logger.LogInformation("Context Key: {Key}, Value: {Value}", prop.Key, prop.Value);
        }

        return Task.FromResult(new ActionResult(true));
    }
}
```

* Register it in `Program.cs`:

  ```csharp
  builder.Services.AddTransient<LogAction>();
  ```
* Map `"action.log"` to the `LogAction` type (using a dictionary or resolver).

---

## **Action Item 2: Implement the Manual Trigger**

This allows **direct execution** of workflows for testing and control.

---

### **2.1. Create the API Endpoint (`Wbskt.Workflow.Service`)**

Create a new controller: `WorkflowTriggersController.cs`.

```csharp
[ApiController]
[Route("api/workflows")]
public class WorkflowTriggersController : ControllerBase
{
    private readonly IWorkflowEngine _workflowEngine;

    public WorkflowTriggersController(IWorkflowEngine workflowEngine)
    {
        _workflowEngine = workflowEngine;
    }

    [HttpPost("{refId}/trigger")]
    public IActionResult TriggerWorkflow(Guid refId, [FromBody] Dictionary<string, object> initialData)
    {
        // Fire and forget — don’t wait for completion
        var context = new WorkflowContext(Guid.NewGuid(), initialData);
        _ = _workflowEngine.ExecuteWorkflowAsync(refId, context);

        return Accepted(); // HTTP 202 - process started
    }
}
```

---

### **2.2. Test the Flow**

1. Create a simple workflow (via `Wbskt.Management.Service`) with one `LogAction` step.
2. Call:

   ```
   POST /api/workflows/{refId}/trigger
   ```
3. Check:

    * Console logs of `Wbskt.Workflow.Service` → should show `LogAction` output.
    * `WorkflowExecutions` table → new execution record logged.

---

## **Action Item 3: Implement the Timed Trigger**

This introduces **scheduled background execution** using Hangfire.

---

### **3.1. Add and Configure Hangfire (`Wbskt.Workflow.Service`)**

1. Add NuGet packages:

    * `Hangfire.AspNetCore`
    * `Hangfire.SqlServer`
2. Configure Hangfire in `Program.cs`:

```csharp
// Add Hangfire services
builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(
        builder.Configuration.GetConnectionString("HangfireConnection"),
        new SqlServerStorageOptions
        {
            // optional config
        }));

// Add Hangfire server
builder.Services.AddHangfireServer();

// Enable dashboard
app.UseHangfireDashboard();
```

---

### **3.2. Create the `WorkflowSchedulerService`**

```csharp
public class WorkflowSchedulerService
{
    private readonly IWorkflowsReader _workflowsReader;
    private readonly IWorkflowEngine _workflowEngine;

    // ... constructor ...

    public async Task TriggerDueWorkflows()
    {
        // 1. Get active workflows with TriggerType == "Timed"
        var timedWorkflows = await _workflowsReader.GetDueTimedWorkflowsAsync();

        foreach (var workflow in timedWorkflows)
        {
            // 2. Check if the CRON expression is due
            var cron = CrontabSchedule.Parse(workflow.TriggerConfiguration);
            var nextOccurrence = cron.GetNextOccurrence(DateTime.UtcNow.AddMinutes(-1));

            if (nextOccurrence <= DateTime.UtcNow)
            {
                // 3. Trigger workflow with empty context
                var context = new WorkflowContext(Guid.NewGuid(), new Dictionary<string, object>());
                _ = _workflowEngine.ExecuteWorkflowAsync(workflow.RefId, context);
            }
        }
    }
}
```

> 🧠 Use **NCrontab.Advanced** to parse and evaluate CRON expressions.

---

### **3.3. Register the Recurring Job**

Add this after the Hangfire dashboard configuration in `Program.cs`:

```csharp
RecurringJob.AddOrUpdate<WorkflowSchedulerService>(
    "workflow-scheduler",
    service => service.TriggerDueWorkflows(),
    Cron.Minutely()); // Run every minute
```

---

## **End of Phase 2**

By the end of this phase:

* The **Workflow Engine** can now execute workflows end-to-end.
* You can **trigger workflows manually** via API.
* **Timed triggers** automatically execute scheduled workflows.
* The system is ready for **Phase 3** — adding more complex triggers, actions, and conditional logic.

---

# **WBSKT Phase 3**

### **Goal**

The goal of this phase is to bridge the gap between the abstract workflow engine and the physical world of your connected devices.
We'll establish the **real-time communication service** and implement the first actions that allow workflows to interact with clients.

---

## **Phase 3: Real-time Communication & Client Actions (Expanded)**

### **Objective**

To establish the real-time communication link with devices, enable workflows to be triggered by device data, and allow workflows to send commands back to devices.

---

## **Action Item 1: Build the `Wbskt.Socket.Service`**

This service is a dedicated, high-performance hub for managing all persistent client connections.

---

### **1.1. Configure for WebSockets**

In `Program.cs` of `Wbskt.Socket.Service`, ensure WebSocket middleware is enabled:

```csharp
var app = builder.Build();
app.UseWebSockets();
// ... other middleware
```

---

### **1.2. Implement the Connection Endpoint**

* Create a new controller or middleware that will handle incoming WebSocket requests (e.g., at the path `/ws`).
* Inside this endpoint's logic:

    1. Check if the request is a WebSocket request.
    2. **Authenticate first** — the client must provide its JWT (obtained during registration) in the header:
       `Authorization: Bearer <token>` or as a query parameter.
    3. Use `.AddJwtBearer()` authentication scheme for clients to validate the token.
       If invalid, reject with **HTTP 401**.
    4. If valid, accept the connection:

       ```csharp
       WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
       ```
    5. Extract the `ClientId` and `ClientUniqueId` from the token’s claims.

---

### **1.3. Create the `ClientConnectionManager`**

* A **singleton service** responsible for tracking active connections.
* Use `ConcurrentDictionary<int, WebSocket>` (or a custom class) to map `ClientId` → `WebSocket`.

Methods:

* `OnConnected` — add `ClientId` and `WebSocket`.
* `OnDisconnected` — remove disconnected clients.
* `GetSocketById` — retrieve active socket by `ClientId`.

---

### **1.4. Implement the Message Loop (Listen for Client Data)**

After a connection is accepted, start a loop to handle messages:

```csharp
while (webSocket.State == WebSocketState.Open)
{
    var result = await webSocket.ReceiveAsync(...);
    // Handle incoming message
}
```

When a message is received:

1. Deserialize the JSON payload.
2. Inject the **Message Queue Publisher** service.
3. Publish a `ClientDataReceivedEvent` to the message queue:

```csharp
public record ClientDataReceivedEvent(int ClientId, Guid ClientUniqueId, string Payload);
```

---

### **1.5. Implement the Command Listener (Listen for Commands from Workflows)**

* Implement as a **background service** (`IHostedService`).
* Subscribe to a queue/topic for `SendCommandToClientEvent`.
* On receiving a command:

    1. Retrieve WebSocket from `ClientConnectionManager`.
    2. If connected → send payload:

       ```csharp
       await webSocket.SendAsync(...);
       ```
    3. If not connected → log or publish `CommandFailedEvent`.

---

## **Action Item 2: Implement Real-time Triggers & Actions (`Wbskt.Workflow.Service`)**

This enables the workflow engine to react to events generated by the Socket Service.

---

### **2.1. Implement the Real-time Trigger Listener**

* Create a background service: `RealtimeTriggerService` (`IHostedService`).
* Subscribe to `ClientDataReceivedEvent` from the message queue.
* When triggered:

    1. Query **Workflows** where `TriggerType = "ClientData"`.
    2. Evaluate `TriggerConfiguration` (e.g., `payload.temperature > 40`) against event data.
    3. If condition matches → trigger the workflow via `IWorkflowEngine`:

```csharp
var initialData = new Dictionary<string, object>
{
    ["trigger_type"] = "ClientData",
    ["client_id"] = receivedEvent.ClientId,
    ["payload"] = deserializedPayload
};

var context = new WorkflowContext(Guid.NewGuid(), initialData);
_ = _workflowEngine.ExecuteWorkflowAsync(workflow.RefId, context);
```

---

### **2.2. Implement the `SendPayloadToClientAction`**

* Create `SendPayloadToClientAction.cs` implementing `IAction`.
* The database `StepConfiguration` defines:

    * Target `ClientId` (or how to resolve it from context).
    * Payload to send.

`ExecuteAsync` method:

1. Read `ClientId` and `Payload` from configuration/context.
2. Inject `Message Queue Publisher`.
3. Publish a `SendCommandToClientEvent`:

```csharp
public record SendCommandToClientEvent(int ClientId, string Payload);
```

Register the action in `Program.cs`:

```csharp
builder.Services.AddTransient<SendPayloadToClientAction>();
```

---

## **Action Item 3: Set Up the Inter-Service Message Queue**

This acts as the **communication backbone** between services.
(Do **not** use an in-memory bus for this stage.)

---

### **3.1. Choose and Run a Message Broker**

For local development, run **RabbitMQ** in Docker:

```bash
docker run -d --hostname my-rabbit --name some-rabbit \
-p 5672:5672 -p 15672:15672 rabbitmq:3-management
```

Access the management UI at: [http://localhost:15672](http://localhost:15672)

---

### **3.2. Create a Common Messaging Library**

In `Wbskt.Common` or a new shared project:

* Define:

  ```csharp
  public interface IMessageBusPublisher { ... }
  public interface IMessageBusSubscriber { ... }
  ```
* Implement using RabbitMQ for:

    * Connection management
    * Exchange/queue declaration
    * Publishing & consuming

This allows easy future migration (e.g., to Azure Service Bus).

---

### **3.3. Integrate into Services**

* **`Wbskt.Socket.Service`**

    * Inject `IMessageBusPublisher` → publish `ClientDataReceivedEvent`
    * Inject `IMessageBusSubscriber` → listen for `SendCommandToClientEvent`

* **`Wbskt.Workflow.Service`**

    * Inject `IMessageBusSubscriber` → listen for `ClientDataReceivedEvent`
    * Inject `IMessageBusPublisher` → publish `SendCommandToClientEvent`

---

## **End of Phase 3 **

By the end of Phase 3:

* `Socket.Service` acts as a **connection manager** for all clients.
* `Workflow.Service` becomes the **intelligent brain**, reacting to data and sending commands.
* The **message queue** serves as the **scalable nervous system**, ensuring decoupled, event-driven communication between services.

---
Here’s your **WBSKT Phase 4** content reformatted into clean, professional **Markdown**, consistent with your Phase 3 document:

---

# **WBSKT Phase 4**

### **Goal**

The goal of this phase is to dramatically increase the power and flexibility of your platform.
We'll add **logical branching** to the workflow engine, open it up to the world with **webhooks**, and build the first **external integrations**.
This is where your product evolves from a specialized IoT tool into a **general-purpose automation platform**.

---

## **Phase 4: Expanding Capabilities & User Experience (Expanded)**

### **Objective**

To add **advanced logic (if-conditions)**, **external triggers (webhooks)**, and **external actions (email, HTTP requests)**, making the platform a versatile automation tool.

---

## **Action Item 1: Implement the If-Condition Modifier (`Wbskt.Workflow.Service`)**

This step adds **intelligence** to workflows — enabling them to take different paths based on runtime data.

---

### **1.1. Update the `WorkflowSteps` Table**

Ensure the `WorkflowSteps.sql` script includes:

* `OnSuccessStepId` *(nullable int)*
* `OnFailureStepId` *(nullable int)*

These columns store the **next step** IDs for branching.
For linear workflows, they can remain `NULL`.

---

### **1.2. Create the `IfConditionModifier`**

* A **special type of action** that directs the workflow engine rather than performing an external task.
* Create a new class: `IfConditionModifier.cs`.
* It can implement `IAction` or a new, more specific `IModifier` interface.

**StepConfiguration must include:**

* `LeftOperand`: Value to check (e.g., `{{context.payload.temperature}}`)
* `Operator`: Comparison type (`GreaterThan`, `Equals`, `Contains`, etc.)
* `RightOperand`: Value to compare against (e.g., `40`)

**`ExecuteAsync` logic:**

1. Parse the configuration.
2. Resolve `LeftOperand` from the `WorkflowContext`.
3. Perform the comparison using the specified `Operator`.
4. Return an `ActionResult` indicating whether the condition was **true** or **false**.

---

### **1.3. Enhance the `WorkflowEngine` to Handle Branching**

Modify the main workflow execution loop (`WorkflowEngine.cs`):

1. Retrieve the current `WorkflowStepRecord`.
2. If the step is an `IfConditionModifier`:

    * If result is **true**, go to step with ID = `OnSuccessStepId`.
    * If result is **false**, go to step with ID = `OnFailureStepId`.
3. If the step is a **regular action** and succeeds:

    * If `OnSuccessStepId` is defined → jump there.
    * Else → continue linearly via `StepOrder`.
4. If the step **fails**:

    * If `OnFailureStepId` is defined → jump there (error handling).
    * Else → halt the workflow and mark as **Failed**.

---

## **Action Item 2: Implement the Webhook Trigger (`Wbskt.Workflow.Service`)**

This enables **external services** to trigger workflows through HTTP calls — opening your platform to the world.

---

### **2.1. Create the `WebhooksController`**

* Does **not** require standard user authentication.
* Define endpoint:
  **`POST /api/webhooks/{webhookId}`**
  where `{webhookId}` is a unique **GUID** per webhook-enabled workflow.

---

### **2.2. Implement the Endpoint Logic**

1. Extract `webhookId` from the route.
2. Use `_workflowsReader` to find a `WorkflowRecord` where:

    * `TriggerType == "Webhook"`, and
    * `TriggerConfiguration` contains this `webhookId`.
3. If not found → return **HTTP 404**.
4. If found:

    * Read the request body (JSON).
    * Deserialize into a dictionary or dynamic object.
    * Build `initialContext` with:

        * Request **body**
        * **Headers**
        * **Query parameters**
    * Trigger the workflow via:

      ```csharp
      _ = _workflowEngine.ExecuteWorkflowAsync(workflow.RefId, context);
      ```
    * Return **HTTP 202 Accepted** immediately (non-blocking).

---

### **2.3. Update Workflow Creation (`Wbskt.Management.Service`)**

When a user creates a workflow with `TriggerType = "Webhook"`:

1. Generate a **new GUID** for `webhookId`.
2. Store this in the workflow’s `TriggerConfiguration`.
3. Return the full webhook URL in the API response, e.g.:

```
https://api.yourproduct.com/api/webhooks/xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
```

The user can then configure this URL in their third-party service.

---

## **Action Item 3: Implement External Actions (`Wbskt.Workflow.Service`)**

These actions prove the platform can **interact with external APIs and services**, not just its own clients.

---

### **3.1. Secure Credential Management**

* Create a **Credentials** or **Integrations** table (encrypted at rest).
* Store credentials linked to each `UserId`.
* Expose secure APIs (in `Wbskt.Management.Service`) for users to manage their secrets (e.g., API keys).
* **Never store secrets** directly in `StepConfiguration`.

---

### **3.2. Create `SendEmailAction.cs`**

* Implements `IAction`.
* Configuration includes:

    * Recipient
    * Subject
    * Body (supports templating: e.g., `Hello {{context.trigger.name}}`)
* Fetch user’s email credentials securely.
* `ExecuteAsync` should:

    1. Render templates using `WorkflowContext`.
    2. Use **SendGrid SDK** (or similar) to send email.
    3. Return success/failure `ActionResult`.

---

### **3.3. Create `MakeHttpRequestAction.cs`**

A **generic, powerful action** to interact with any HTTP API.

**Configuration fields:**

* `URL`
* `Method` (`GET`, `POST`, etc.)
* `Headers` (JSON)
* `Body` (JSON)
* All fields can be templated.

**`ExecuteAsync` logic:**

1. Construct `HttpRequestMessage`.
2. Render templates in URL, headers, and body.
3. Send request using `HttpClient`:

   ```csharp
   await httpClient.SendAsync(request);
   ```
4. Capture the response (status, body).
5. Store response data in the workflow context, e.g.:

   ```
   context.steps.my_http_step.response
   ```

---

## **Action Item 4: Frontend / User Experience (Preview)**

While the full UI comes later, the backend must support these new workflow features.

---

### **4.1. Update `WorkflowsController`**

* The `POST /api/workflows` endpoint should now accept **complex JSON**:

    * Defines full **workflow graphs**, including step types, identifiers, configurations, and branching paths.
* Enables frontend or advanced users to construct and manage **complete workflow structures**.

---

## **End of Phase 4 **

By the end of **Phase 4**:

* Your platform supports **conditional logic**, **external triggers**, and **integrations**.
* You’ve transitioned from an IoT-specific system into a **general-purpose automation engine** — comparable to **Zapier** or **Make**, but with the unique advantage of **real-time device integration**.
* You can now appeal to a **wider audience** — from smart home hobbyists to businesses automating complex processes.

---