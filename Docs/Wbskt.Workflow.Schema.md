# WBSKT Workflow Definition Schema Specification

This document details the JSON schema and object model for WBSKT Workflows. The system uses a graph-based representation consisting of **Nodes**, **Edges**, and a recursive **Expression AST**.

---

## 1. Top-Level Workflow Definition
The root object that defines a workflow's metadata and its logic graph.

| Property | Type | Description |
| :--- | :--- | :--- |
| `WorkflowRefId` | `Guid` | The unique public reference ID for the workflow. |
| `WorkspaceId` | `int` | The internal ID of the workspace this workflow belongs to. |
| `Name` | `string` | User-defined name. |
| `Description` | `string` | User-defined description. |
| `IsEnabled` | `bool` | Whether the workflow is active and listening for triggers. |
| `Version` | `int` | Version number (for optimistic concurrency/history). |
| `Nodes` | `List<BaseNode>` | Polymorphic list of logic units in the graph. |
| `Edges` | `List<WorkflowEdge>` | Connectivity map between node ports. |
| `Concurrency` | `enum` | Policy for simultaneous triggers (`AllowParallel`, `Queue`, `CancelExisting`). |
| `InitialState` | `Dictionary` | Key-value pairs seeded into the workflow's memory at start. |
| `CreatedAt` | `DateTime` | Creation timestamp. |

---

## 2. Nodes (`BaseNode`)
Nodes are polymorphic objects identified by the `$type` property.

### Common Node Properties
*   `NodeId` (Guid): Unique identifier within the workflow.
*   `Name` (string): Label for the node.
*   `Ports` (List): Definitions of `in` and `out` handles.

### 2.1 Trigger Nodes
Triggers are the entry points of a workflow.

| $type | Node Class | Specific Properties |
| :--- | :--- | :--- |
| `trigger:device` | `DeviceTriggerNode` | `ClientRefId` (Guid), `TriggerType` (Telemetry/PropChange), `PropertyName` (string) |
| `trigger:timer` | `TimerScheduleNode` | `Interval` (int), `Unit` (Seconds/Minutes/Hours/Days) |

### 2.2 Action Nodes
Actions perform operations in the external world or system.

| $type | Node Class | Specific Properties |
| :--- | :--- | :--- |
| `action:command` | `SendCommandActionNode` | `TargetClientRefId` (Guid), `MessageType` (string), `Payload` (JSON string) |
| `action:email` | `EmailNotificationNode` | `Recipient`, `Subject`, `Body` |
| `action:telegram` | `TelegramNotificationNode` | `ChatId`, `Message` |
| `action:toast` | `ToastNotificationNode` | `Title`, `Message` |
| `action:webhook` | `WebhookNotificationNode` | `Url`, `Method`, `Payload` |

### 2.3 Control Nodes
Controls manage the flow of execution and internal state.

| $type | Node Class | Specific Properties |
| :--- | :--- | :--- |
| `control:delay` | `DelayNode` | `Seconds` (int) |
| `control:logic` | `LogicGateNode` | `Condition` (WorkflowExpression) |
| `control:loop` | `LoopNode` | `ItemsExpression` (Expression), `IteratorName` (string) |
| `control:variable` | `VariableNode` | `VariableName`, `Expression`, `Operation` (Set/Get/Inc/Dec) |

---

## 3. The Expression AST (`WorkflowExpression`)
Used for dynamic logic evaluation. Identified by the `type` property.

| type | Expression Class | Description | Properties |
| :--- | :--- | :--- | :--- |
| `literal` | `LiteralExpression` | A fixed value. | `value` (JsonElement) |
| `access` | `MemberAccessExpression`| Resolves paths. | `path` (e.g., `$trigger.payload.temp`) |
| `unary` | `UnaryExpression` | Single operand op. | `op` (Not, IsNull, etc.), `operand` |
| `binary` | `BinaryExpression` | Two operand op. | `op` (Add, Eq, And, etc.), `left`, `right` |
| `function`| `FunctionExpression` | Built-in logic. | `fn` (Now, Round, Count, etc.), `args` |

---

## 4. Connectivity (Edges & Ports)

### `WorkflowEdge`
*   `EdgeId` (Guid): Unique ID for the connection.
*   `Source`: `WorkflowPort` (NodeId + PortId).
*   `Target`: `WorkflowPort` (NodeId + PortId).
*   `Properties`: Dictionary for UI metadata (e.g., labels like "HIGH BATTERY").

### `PortNames` (Constants)
*   `in`, `out` (Standard Flow)
*   `match`, `otherwise` (Logic Gates)
*   `body`, `completed` (Loops)
*   `on_response_change` (Webhooks)

---

## 5. Execution Policies (`Enums`)

### WorkflowConcurrencyPolicy
*   `0: AllowParallel`: Every trigger spawns a new instance immediately.
*   `1: Queue`: Simultaneous triggers wait for the current instance to finish.
*   `2: CancelExisting`: A new trigger kills the currently running instance.

### VariableOperation
*   `Set`, `Get`, `Increment`, `Decrement`
