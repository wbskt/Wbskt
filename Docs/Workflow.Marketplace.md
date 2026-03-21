# WBSKT Cloud Marketplace Architecture
## Universal Integration & Workflow Orchestration

This document outlines the architectural transformation of WBSKT from a standalone service into a multi-tenant Cloud SaaS platform. The goal is to support a **Marketplace** where third-party developers can build, publish, and monetize custom workflow nodes and "Virtual Client" integrations.

---

## 1. Core Vision: The "Agnostic Orchestrator"
In the new architecture, the **Workflow Engine** is a "blind" executor. It does not know what a "Tesla" or "Slack" node is. Instead, it operates on a **Manifest-Driven System**.

- **The Engine:** Executes logic, manages state, and routes data.
- **The Marketplace:** Provides the definitions (UI) and the execution logic (Code) for specific nodes.
- **The IDE (UI):** Dynamically renders node configurations based on the marketplace manifests.

---

## 2. The Integration Manifest (`manifest.json`)
The manifest is the "Source of Truth" for any marketplace contribution. It defines how a node looks in the IDE and how it behaves in the cloud.

### Sample Structure:
```json
{
  "id": "wbskt-stripe-connector",
  "version": "2.1.0",
  "author": "Stripe Dev Team",
  "executionModel": "wasm", 
  "config": {
    "apiKey": { "type": "password", "required": true }
  },
  "nodes": [
    {
      "id": "capture-payment",
      "label": "Capture Payment",
      "category": "Payments",
      "inputs": {
        "amount": { "type": "number", "mapping": "data.amount" },
        "currency": { "type": "string", "default": "USD" }
      },
      "outputs": {
        "chargeId": "string",
        "receiptUrl": "string"
      }
    }
  ]
}
```

---

## 3. The Three-Tiered Execution Model
To support thousands of tenants safely, marketplace code is executed in three isolated tiers based on the node's requirements.

### Tier A: Logic Nodes (WASM Sandbox)
- **Target:** Data mappers, math, filters, JSON processing.
- **Tech:** **WebAssembly (WASM)** via `Wasmtime`.
- **Isolation:** CPU/Memory sandboxed. No network/disk access.
- **Performance:** Sub-millisecond execution. Extremely cost-effective for Cloud SaaS.

### Tier B: Connector Nodes (Containerized Sidecars)
- **Target:** Integrations requiring persistent connections (SQL, MQTT, long-lived WebSockets).
- **Tech:** **Kubernetes Sidecars** or **Docker Isolation**.
- **Isolation:** Each user workspace gets a dedicated "Integration Pod" that hosts their installed connectors.
- **Security:** Network policies restrict these pods to only talk to WBSKT core services and the specific 3rd-party API.

### Tier C: Function Nodes (Serverless / MicroVMs)
- **Target:** Heavy lifting, custom scripts (Python/Node.js), image processing.
- **Tech:** **Firecracker MicroVMs** (AWS Lambda style).
- **Isolation:** Hardware-level isolation for executing untrusted user-written code.

---

## 4. Virtual Clients: The Integration Bridge
Virtual Clients represent 3rd-party services as if they were physical WBSKT hardware devices.

### A. Inbound Gateway (Webhooks)
- **Service:** `Wbskt.Gateway.Service`
- **Function:** Provides a public endpoint (`hooks.wbskt.com/v1/{workspace}/{clientId}`).
- **Flow:** 
    1. Stripe sends a webhook.
    2. Gateway resolves the `WorkspaceID`.
    3. Gateway applies a **WASM Transformation** (defined in the marketplace) to convert Stripe JSON into a standard `Wbskt.TelemetryEvent`.
    4. The Workflow Engine triggers immediately.

### B. Outbound Adapters (Polling)
- **Service:** `Wbskt.Polling.Worker`
- **Function:** A high-scale scheduler that triggers "Virtual Client" reads.
- **Flow:**
    1. Scheduler triggers every 10m for "Weather Integration."
    2. The Polling Worker executes the marketplace-defined "Read" logic.
    3. Results are pushed into the event bus as new telemetry.

---

## 5. Multi-Tenant Cloud Infrastructure
To ensure "Rider/Postman" IDE performance across thousands of users, the backend is split into specialized hosts:

| Service | Responsibility |
| :--- | :--- |
| **Wbskt.Registry.Service** | The "App Store." Serves node manifests to the IDE. |
| **Wbskt.Management.Host** | Manages Workspaces, Secrets (API Keys), and Permissions. |
| **Wbskt.Workflow.Engine** | High-speed orchestrator. Uses RabbitMQ for node-to-node routing. |
| **Wbskt.Socket.Host** | Manages real-time connections (Physical & Virtual Clients). |
| **Wbskt.State.Store** | Distributed Redis cache for "Live Workflow State." |

---

## 6. Developer Experience (DX)
To build a marketplace, the platform must provide:

1. **WBSKT CLI:** Scaffolds integrations, compiles WASM, and tests manifests locally.
2. **Standard Library:** A set of C#/.NET abstractions for marketplace developers to use (e.g., `BaseNode`, `BaseVirtualClient`).
3. **The Sandbox IDE:** A "Developer Mode" in the main UI where devs can side-load unreleased manifests for testing.
4. **Credential Injection:** A secure way for the engine to inject `{{secrets.apiKey}}` into marketplace code without the engine ever logging the value.

---

## 7. Data Isolation & Security
- **Tenant Isolation:** Every RabbitMQ message includes a `TenantId` header. Consumers are scoped to only process messages for their assigned tenants.
- **Secret Management:** Integration API keys are encrypted at rest using **AES-256-GCM** with a per-workspace master key stored in **Azure Key Vault / AWS KMS**.
- **Rate Limiting:** Every marketplace node execution is throttled per workspace to prevent "infinite loop" workflows from crashing the cloud.
