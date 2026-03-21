# WBSKT Workflow Engine Technical Specification

This document details the architecture, data models, and execution flow of the WBSKT Workflow Engine.

---

## 1. The Blueprint: `WorkflowDefinition`

A `WorkflowDefinition` is a static "blueprint" or "script" created by a user. It defines **what** should happen but does not contain any live data.

### Components:
- **Nodes**: Individual units of logic (Triggers, Logic, Actions). Each node has a unique `NodeId` (Guid) and a `Name`.
- **Edges**: The "wires" connecting nodes. An edge points from a **Source Port** on one node to a **Target Port** on another node.
- **Initial State**: A dictionary of default variables (constants) available to every instance of this workflow.

### Polymorphism:
Nodes are represented using a polymorphic hierarchy. When serialized to JSON, a `$type` property identifies the specific node class (e.g., `trigger:device`, `action:email`).

---

## 2. Connectivity: Ports & Edges

Connectivity is decoupled from node logic using a **Port-based Graph**.

### `PortDefinition`
Every node defines its available "handles" in its constructor. 
- **PortId**: A unique string identifier (e.g., `in`, `out`, `match`, `otherwise`).
- **Direction**: `In` (receives execution) or `Out` (triggers next steps).

### `WorkflowEdge`
An edge represents a connection: `(SourceNode, SourcePort) -> (TargetNode, TargetPort)`.
- **Fan-out**: A single `Out` port can have multiple edges connected to it. This naturally creates **Parallel Branches**.
- **Labels**: Edges can have properties (like a "Battery" label) for UI visualization.

---

## 3. The Memory: Context Hierarchy

Execution relies on three distinct types of "Context" to manage data isolation and persistence.

### A. `BaseTriggerContext` (The Event)
Represents the external event that woke up the engine. It is **Typed** to ensure the engine only activates relevant branches.
- **`ClientPayloadTriggerContext`**: Contains raw telemetry data (JSON).
- **`ClientPropertyChangeTriggerContext`**: Contains specific property names and their new values.

### B. `WorkflowInstance` (The Container)
The record of a single execution run (currently in-memory).
- **`InstanceId`**: Unique to this specific performance.
- **`State`**: Live memory (variables) shared by all parallel branches within this instance.
- **`Pointers`**: A list of `ExecutionPointer` objects tracking exactly where the engine is currently working.

### C. `ExecutionContext` (The Actor's API)
A short-lived, thread-safe wrapper handed to a Node Executor during processing.
- **`TriggerContext`**: Allows the node to see what started the flow.
- **`LastNodeOutput`**: Direct data passed from the immediately preceding node (chaining).
- **`GetState/SetState`**: Safe methods to read/write the shared `WorkflowInstance` memory.

---

## 4. The Brain: Workflow Engine Execution Flow

The `WorkflowEngine` is a reactive coordinator. It follows a strict **Start -> Match -> Execute -> Navigate** cycle.

### Step 1: Ignition (`StartAsync`)
1. Receives a `WorkflowDefinition` and a `BaseTriggerContext`.
2. Creates a new `WorkflowInstance`.
3. **Trigger Matching**: It scans all `BaseTrigger` nodes in the definition and calls `NodeMatchesContext()`.
   - *Example*: Only a node configured for `Device_A` and `OnTelemetry` will match a telemetry event from `Device_A`.
4. Creates an `ExecutionPointer` for every matching trigger node.

### Step 2: The Execution Loop (`ExecutePointerAsync`)
The engine processes every `Active` pointer in a loop:
1. **Resolution**: Uses **Keyed Dependency Injection** to find the `IWorkflowNodeExecutor` matching the node's class name (e.g., `LogicGateExecutor`).
2. **Evaluation**: The node executor runs its logic. If the node contains a `WorkflowExpression` (the AST), it uses the `WorkflowExpressionEvaluator` to resolve paths like `$trigger.payload.temp`.
3. **Result**: The node returns a `NodeExecutionResult`.

### Step 3: Branching & Fan-out
Based on the `NodeExecutionResult`:
- **Faulted**: The pointer is marked as `Faulted` and stops.
- **Waiting**: The pointer is marked as `Waiting` with a `ResumeAt` timestamp (for `DelayNode`). (Note: Resume logic is currently pending persistence implementation).
- **Success**: The engine looks up all `Edges` where `Source.PortId` matches the ports activated by the node.
  - **Single Edge**: The current pointer moves to the `TargetNode`.
  - **Multiple Edges**: The engine "Forks." It reuses the current pointer for the first edge and spawns **New Parallel Pointers** for the others.

### Step 4: Completion
The engine checks the instance after every step. If all pointers are `Completed` or `Faulted`, the `WorkflowInstance` is marked as `Completed` and its `FinishedAt` timestamp is set.

---

## 5. Expression Evaluation (The Logic)

Expressions use a **Recursive Arity Model**:
- **Literal**: Fixed values.
- **MemberAccess**: Resolves paths (`$trigger`, `$state`, `$output`) using dot-notation.
- **Unary**: Operations like `NOT` or `IsNull`.
- **Binary**: Math (`+`, `-`), Comparison (`>`, `==`), and Logic (`AND`, `OR`).
- **Function**: Built-in helpers like `NOW()`, `ROUND()`, or `COUNT()`.

The evaluator is **Synchronous** and **In-Memory**, ensuring high-performance logic checks during the execution loop.
