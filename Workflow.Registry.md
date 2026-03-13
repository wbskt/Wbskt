# WBSKT Registry Host Specification
## The Central Nervous System of the Marketplace

The `Wbskt.Registry.Host` is the authoritative service responsible for managing the WBSKT ecosystem's "Librarian" and "App Store" functions. It acts as the bridge between developers (who build nodes) and the Workflow Engine/IDE (which consume them). 

In a cloud-scale SaaS architecture, the Registry ensures that every integration and logic node is secure, versioned, and accurately represented in the user interface.

---

## 1. Core Responsibilities

### 1.1 Manifest & Schema Authority
The Registry is the primary source of truth for the `manifest.json` files that define the identity of every Marketplace item.
*   **Dynamic UI Generation:** It provides the JSON-Schema required by the IDE to render node property panels (inputs, outputs, dropdowns, and validation rules).
*   **Type Safety:** It enforces the WBSKT Type System, ensuring that a "String" output from Node A can only be connected to a "String" input on Node B.
*   **Manifest Validation:** It rejects any contribution that lacks required metadata (Author, Version, License, Execution Model).

### 1.2 Versioning & Semantic Lifecycle
The Registry manages the evolution of code over time, preventing breaking changes from destroying active workflows.
*   **Semantic Versioning (SemVer):** It tracks major, minor, and patch versions (e.g., `v1.2.0`).
*   **Immutability:** Once a version (e.g., `v1.0.1`) is published, its binary and manifest are locked. Any change requires a new version number.
*   **Status Management:**
    *   `Draft`: Visible only to the developer.
    *   `Published`: Globally available.
    *   `Deprecated`: Hidden from new searches but remains functional for existing workflows.
    *   `Revoked`: Immediately disabled across the entire cloud due to security or critical bugs.

### 1.3 Artifact Repository (Binary Storage)
The Registry handles the physical storage and distribution of the code execution units.
*   **WASM Storage:** Hosts the compiled `.wasm` binaries for high-performance logic nodes.
*   **Asset Hosting:** Stores non-code assets such as SVG icons, screenshots, and Markdown documentation for the Marketplace web view.

### 1.4 Security & Code Verification
The Registry acts as the "Gatekeeper" for the WBSKT Cloud.
*   **Static Binary Analysis:** Scans WASM files for "Illegal Imports." It ensures a node doesn't attempt to call unauthorized host functions (e.g., trying to access the server's `/etc/passwd`).
*   **Digital Signing:** Upon successful validation, the Registry signs the binary with a private platform key.
*   **Execution Verification:** The `Workflow.Engine.Host` will only execute binaries that carry a valid Registry signature, preventing "Man-in-the-Middle" code injection.

### 1.5 Discovery & Marketplace API
The Registry provides the backend for the "Node Library" in the IDE.
*   **Search & Indexing:** Provides full-text search across names, descriptions, and tags (e.g., "IoT", "Finance", "Utilities").
*   **Categorization:** Organizes nodes into logical groups (Triggers, Actions, Logic, Converters).
*   **Popularity Metrics:** Tracks install counts and usage frequency to drive "Featured" and "Trending" sections.

### 1.6 Dependency & Compatibility Mapping
*   **SDK Pinning:** Tracks which version of the WBSKT SDK a node was built against. It prevents a node designed for `v2.0` from being loaded into an Engine running `v1.0`.
*   **Cross-Node Dependencies:** Manages shared WASM modules or utility libraries required by multiple integrations.

---

## 2. Key API Endpoints (Technical Contract)

| Endpoint | Method | Description |
| :--- | :--- | :--- |
| `/v1/nodes/search` | `GET` | Used by the IDE to find nodes by name/tag. |
| `/v1/nodes/{id}/manifest` | `GET` | Returns the JSON schema for UI rendering. |
| `/v1/nodes/{id}/{version}/binary` | `GET` | Used by the Workflow Engine to download the executable. |
| `/v1/publish` | `POST` | Used by the WBSKT CLI to upload a new manifest and binary. |
| `/v1/developer/register` | `POST` | Onboards a new marketplace developer. |

---

## 3. Interaction Flow Example

### Step 1: The IDE Request
A user drags a "Slack" node onto the canvas. The IDE calls `GET /nodes/slack-integration/manifest`. The Registry returns the schema defining a "Channel ID" text box and a "Message" text area.

### Step 2: The Execution Request
A workflow is triggered. The `Workflow.Engine.Host` sees it needs to run `slack-action:v2.1.0`. It calls `GET /nodes/slack-action/2.1.0/binary`. 

### Step 3: Verification
The Engine receives the WASM file and the signature. It verifies the signature against the Registry's public key before loading the WASM into the sandbox.

---

## 4. Multi-Tenant Considerations
*   **Private Nodes:** Allows companies to publish "Internal Only" nodes that are hidden from the global marketplace but visible to their own employees.
*   **Regional Caching:** In a global cloud, the Registry syncs binaries to regional edge caches (CDNs) so that Workflow Engines can download them with minimal latency.
