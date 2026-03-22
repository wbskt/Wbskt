# WBSKT Workflow Engine: Exhaustive Technical Audit

This document provides a deep-dive analysis of the current Workflow Engine implementation, identifying critical bugs, architectural gaps, and compliance issues with project conventions.

---

## 1. Critical Bugs & Logic Errors

### 1.1 The Identity Mismatch (High Severity)
*   **Location:** `WorkflowEngine.cs` (Line 52)
*   **Issue:** `instance.WorkflowId = definition.WorkspaceId;`
*   **Impact:** This maps the `WorkflowId` (internal DB reference) to the `WorkspaceId`. Any logic attempting to query "All instances for Workflow X" will instead return "All instances for all workflows in Workspace Y".

### 1.2 Client ID Resolution (Action Blocked)
*   **Location:** `SendCommandActionExecutor.cs`
*   **Issue:** The executor sends `0` as the internal `ClientId`.
*   **Impact:** The `Socket.Host` will likely reject the command as it cannot route a payload to "Client 0".
*   **Requirement:** The `WorkflowDefinition` must include the internal `ClientId` during the "Save" operation in the Management API, or the Executor must use an `IClientProvider` to resolve it.

### 1.3 Trigger Payload Parsing (Potential Exception)
*   **Location:** `ClientPropertyChangeTriggerHandler.cs`
*   **Issue:** `JsonSerializer.Deserialize<JsonElement>(payload)` is called on a raw string.
*   **Impact:** If the property change is a simple string (not a JSON object), the deserializer will throw. While caught, it results in unnecessary exception overhead for every telemetry packet.

---

## 2. Architectural Risks

### 2.1 Captive Dependency (System Stability)
*   **Location:** `WorkflowEngine.cs`
*   **Issue:** The engine injects `IEventBus` directly into the constructor.
*   **Risk:** If `WorkflowEngine` is registered as a **Singleton** and `IEventBus` (MassTransit) is **Scoped**, the application will fail to start or throw an `ObjectDisposedException` after the first request.
*   **Fix:** Always resolve the `IEventBus` from the `IServiceScope` created inside `ExecutePointerAsync`.

### 2.2 Unmanaged Task Leakage
*   **Location:** `WorkflowEngine.cs`
*   **Issue:** `_ = ExecutePointerAsync(...)` starts an unmanaged Task.
*   **Risk:**
    1.  **Scaling:** If the host is scaled down, these tasks are terminated instantly.
    2.  **Monitoring:** There is no "Registry" of active tasks, making it impossible to see if the engine is currently overloaded.
*   **Fix:** Implement a `TaskTracker` or `ActiveInstanceRegistry` to manage the lifecycle of these tasks.

### 2.3 Lack of Cancellation Tokens
*   **Location:** `WorkflowEngine.cs`
*   **Issue:** None of the execution loops support `CancellationToken`.
*   **Risk:** You cannot "Stop" a workflow that has entered an infinite loop or is waiting on a long delay without restarting the entire service.

---

## 3. Convention & Style Violations

### 3.1 Immutable Returns
*   **Violation:** `NodeExecutionResult` uses `List<string> ActivatedPortIds { get; set; }`.
*   **Convention:** Returns should use `IReadOnlyCollection<T>` to signal immutability.

### 3.2 Reference Mapping Boundary
*   **Violation:** `ClientPropertyChangeTriggerHandler` uses `ClientRefId` to look up workflows directly.
*   **Convention:** While acceptable for internal event handlers, the lookup key `"client:{guid}"` should be abstracted into a constant or a helper to ensure consistency with the `Socket.Host`.

---

## 4. Live Status & Observability Plan

To fulfill the request for "Live Status", the following must be implemented:

### 4.1 The "Materialized View" Pattern
Do not query the `WorkflowEngine` for status. Instead:
1.  **State Service:** A new service (likely in Management Host) consumes `WorkflowInstanceStartedEvent` and `NodeExecutionEvents`.
2.  **Redis Store:** Store the current "Active Node" and "State" in Redis under the key `wbskt:instance:{instanceRefId}`.
3.  **TTL:** Set a 24-hour TTL on these records to auto-clean completed flows.

### 4.2 API Requirements
*   `GET /v1/monitoring/instances/active`: Filtered by `WorkspaceId`.
*   `GET /v1/monitoring/instances/{refId}`: Returns:
    *   `StartTime`
    *   `CurrentNode` (from the most recent `NodeExecutionStartedEvent`)
    *   `ExecutionState` (JSON variables)
    *   `Status` (Running, Waiting, Faulted)
*   `GET /v1/monitoring/instances/{refId}/history`: Returns the chronological "Breadcrumb" trail:
    *   Sequence of executed `NodeId`s.
    *   Timestamps for entry/exit of each node.
    *   Success/Failure results and error messages for each step.

---

## 5. Feature Gap Analysis

### 5.1 Typed Data Edges
*   **Missing:** Currently, all data is passed via `LastNodeOutput` (object).
*   **Future:** Implement `DataPort` types (String, Number, Bool) to allow the UI to validate connections (e.g., preventing a "Temperature" number from being plugged into an "Email Address" field).

### 5.2 Global Workflow Variables
*   **Missing:** Workflows only have "Instance State".
*   **Future:** Support "Workspace Variables" (e.g., `MaxTemperatureThreshold`) that can be updated via API and immediately affect all running workflows.

### 5.3 Step-Through Debugger
*   **Missing:** Engineers cannot "pause" a live workflow.
*   **Future:** Add a `BreakpointNode`. When hit, the engine publishes a `WorkflowPausedEvent` and waits for a `ResumeAsync` command from the Dashboard.

---

## 6. Phase 2 Discovery (Edge Cases & Concurrency)

### 6.1 Ignored Concurrency Policy (Race Condition Risk)
*   **Location:** `WorkflowEngine.StartAsync`
*   **Issue:** The engine ignores `definition.Concurrency`. If a device spams telemetry, the engine spawns infinite parallel workflows even if the user configured it to `Queue` or `Cancel Previous`.
*   **Impact:** Hardware devices could receive conflicting commands simultaneously.

### 6.2 The Missing `WorkflowId` Property
*   **Location:** `WorkflowDefinition.cs`
*   **Issue:** The model lacks the internal database `Id`. The mapping from `WorkflowEntity` discards it, meaning the engine only knows the `RefId`.

### 6.3 Shallow Copy of Initial State
*   **Location:** `WorkflowEngine.cs` (Line 41)
*   **Issue:** `State = new Dictionary<string, object?>(definition.InitialState)` creates a shallow copy.
*   **Impact:** Complex objects in the initial state are passed by reference. Mutating a state variable in Instance A will alter the state in Instance B concurrently.

### 6.4 Missing Fan-In (Join) Mechanics
*   **Location:** `ExecutePointerAsync` edge routing.
*   **Issue:** If two parallel branches converge on a single target node, the target node will execute twice (once for each arriving pointer) instead of waiting for both branches to synchronize.

### 6.5 Silent Death on Event Bus Failure
*   **Location:** `FailPointerAsync` inside `ExecutePointerAsync` catch block.
*   **Issue:** If a workflow faults, and the Event Bus is currently down, publishing the `NodeExecutionFailedEvent` throws an exception. Because the Task is unmanaged, it dies silently. The instance is never marked as `Failed` and remains a permanent memory leak.
