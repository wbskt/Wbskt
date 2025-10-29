WBSKT New UI
It’s an expansion of everything we discussed — focusing on how the new developer-focused, browser-based UI should look, behave, and be implemented in your Angular + React Flow architecture.

⸻

🧠 WBSKT New UI — Developer Studio Design Document

🎯 Overview

The WBSKT Studio UI is a developer-first, browser-based IDE for building, managing, and debugging automation workflows.
It combines:
	•	Angular for the application shell (dashboard, routing, management, state)
	•	React Flow for the visual workflow builder
	•	Tailwind + shadcn/ui for consistent, modern design

The design philosophy is inspired by Postman, VS Code, and JetBrains Rider — giving developers a familiar, high-performance environment inside the browser.

⸻

🧱 1. Layout Structure

+-----------------------------------------------------------------------------------+
| [Top Command Bar]                                                                 |
+-----------------------------------------------------------------------------------+
| [Sidebar]  | [Main Workspace (Tabs: Workflows, Executions, Policies, etc.)]       |
|            |----------------------------------------------------------------------|
|            | [Bottom Console / Logs / Network Panel]                              |
+-----------------------------------------------------------------------------------+

Layout Rules
	•	Desktop-first layout, responsive down to tablet screens.
	•	Uses CSS grid or flex-based docking layout.
	•	Sidebar and bottom panels can collapse, resize, or float.
	•	Dark mode is default; light mode toggle available.

⸻

🧭 2. Top Command Bar

A slim, persistent header inspired by VS Code’s command palette.

Sections:
	•	Left: App logo → Dashboard/Home
	•	Center: Search / Command palette (Ctrl+K)
	•	Right: Run ▶, Save 💾, Theme toggle 🌙, Connection Status (API/WebSocket), Profile menu

Behavior:
	•	Sticky at top.
	•	Shows active workspace name or workflow title.
	•	Displays status (Connected / Reconnecting / Offline).

⸻

📂 3. Sidebar Navigation

Primary navigation area, similar to VS Code’s activity bar.

Tabs:
	1.	Workflows – list of all workflows (searchable)
	2.	Clients / Devices – connected clients with status
	3.	Executions – recent workflow runs
	4.	Node Library – draggable triggers, modifiers, actions
	5.	Integrations / Settings – manage environment, credentials

Features:
	•	Icon-based vertical bar (collapsible)
	•	Each section opens a secondary panel with detailed view
	•	Persistent state (open tab remembered)

⸻

🧩 4. Main Workspace

The heart of the studio — where users interact with workflows, data, and results.

Tab Types:
	•	Workflow Editor → React Flow canvas
	•	Execution Viewer → Logs and data from a workflow run
	•	Policy Editor → JSON-based editor for client policies
	•	API Tester → Built-in REST tester (Postman-style)
	•	Docs → Markdown viewer (inline reference)

Behavior:
	•	Tabs can be closed/reopened (like IDE tabs)
	•	Unsaved changes indicator (●)
	•	Keyboard shortcuts:
	•	Ctrl+S → Save
	•	Ctrl+Tab → Switch tab
	•	Ctrl+W → Close tab
	•	Ctrl+R → Run workflow

⸻

🧠 5. Workflow Editor (React Flow)

Layout

┌───────────────────────────────────────────────────────────────────────────────┐
│ [Toolbar: Run ▷ | Save 💾 | Fit View | Zoom In/Out | Undo/Redo]               │
├───────────────────────────────────────────────────────────────────────────────┤
│ [Node Library] | [Canvas (React Flow)] | [Node Inspector / Config Panel]      │
├───────────────────────────────────────────────────────────────────────────────┤
│ [Console / Output / Variables Watch]                                          │
└───────────────────────────────────────────────────────────────────────────────┘

Core Features
	•	Drag & drop from Node Library.
	•	Connect nodes to define data flow.
	•	Real-time validation (invalid connections highlighted).
	•	Inline node search (Ctrl+E).
	•	Mini-map and grid snapping.

Node Inspector (Right Panel)
	•	Shows configuration for selected node.
	•	Tabs:
	•	Properties
	•	Code View (JSON/YAML)
	•	Preview (Resolved Values)

⸻

🪶 6. Bottom Console

Multi-tab debugging panel for developers:
	•	Console: Real-time logs during workflow execution
	•	Network: Shows outgoing/incoming HTTP calls
	•	Variables Watch: Displays data flowing between nodes
	•	Errors: Lists runtime errors and validation issues

Toggle: Ctrl+~
Resizable: Drag splitter between main area and bottom panel.

⸻

🧰 7. Integrating Angular & React

Architectural Model

Layer	Tech	Role
Angular	App shell, routing, state, services	Navigation, API integration
React (React Flow)	Workflow canvas	Node editing, layout, logic
Shared Libraries	TypeScript models, utils, UI tokens	Consistency across both

Integration Mechanism
	•	React Flow app exported as Web Component using react-to-webcomponent
	•	Angular imports <wbskt-react-flow> element dynamically
	•	Communication via:
	•	Inputs: [workflow] prop (Angular → React)
	•	Events: (updated) output (React → Angular)

⸻

🔗 8. Data Flow

+---------------------+
| Angular Shell       |
|  - Routes           |
|  - State Mgmt       |
|  - API Client       |
+----------▲----------+
           |
           | JSON workflow data (input/output)
           ▼
+---------------------+
| React Flow Canvas   |
|  - Node graph       |
|  - Inspector        |
|  - Local state      |
+---------------------+

Workflow Save Cycle:
	1.	User edits in React Flow
	2.	React emits workflowUpdated event
	3.	Angular receives it → updates store → calls backend /api/workflows/:id

⸻

🧠 9. UX & Performance Optimizations

Feature	Description
Virtualized Panels	Lazy-load content and nodes for large workflows
Local Cache	IndexedDB for unsaved workflows and state recovery
Keyboard-first Navigation	Command palette and shortcuts throughout
PWA Ready	Installable browser app with offline UI cache
Auto Reconnect	WebSocket recovery and retry logic
Theme Sync	Shared CSS variables across Angular and React


⸻

🧩 10. Technology Stack Summary

Area	Library / Tool
Framework	Angular 18 (standalone components)
Visual Editor	React 19 + React Flow
UI System	TailwindCSS + shadcn/ui + Lucide Icons
State Management	Angular Signals + Zustand
Routing	Angular Router + lazy modules
Communication	Web Components + CustomEvent bridge
Storage	IndexedDB via Dexie.js
Build	Vite (for React), Angular CLI (for Angular)
Testing	Jest + Playwright
Hosting	Nginx / CloudFront / Azure Static Web Apps


⸻

🗺️ 11. Implementation Roadmap

Phase	Focus	Key Deliverables
Phase 1	Angular Shell	Top bar, sidebar, dashboard, API integration
Phase 2	React Flow Integration	Embed studio, load/save workflows
Phase 3	Node Inspector & Library	Drag/drop, editing UI
Phase 4	Logs, Execution Viewer	Realtime logs via WebSocket
Phase 5	UX Polish & PWA	Shortcuts, offline mode, themes


⸻

✨ 12. Visual Style & Aesthetic
	•	Dark theme default, inspired by VS Code
	•	Monospace font: JetBrains Mono / Fira Code
	•	Accent colors: Electric Blue (#4A9EFF), Slate Gray background
	•	Iconography: Lucide icons
	•	Animations: Minimal; use Framer Motion or Angular animations

⸻

🧩 13. Component Structure & Implementation Breakdown

⸻

🗂️ A. Base Repository Structure

Your workspace should follow a monorepo layout, compatible with Angular CLI, Nx, or plain workspaces.
This keeps Angular (shell), React Flow (studio), and shared libraries in one versioned ecosystem.

/wbskt-studio/
│
├── angular.json                  # Angular workspace config
├── package.json
├── tsconfig.base.json
├── nx.json                       # Optional (if using Nx)
│
├── projects/
│   ├── angular-shell/             # Angular main app
│   │   ├── src/
│   │   │   ├── app/
│   │   │   ├── assets/
│   │   │   └── environments/
│   │   └── angular.json
│   │
│   ├── react-flow-studio/         # React-based workflow builder
│   │   ├── src/
│   │   │   ├── components/
│   │   │   ├── hooks/
│   │   │   ├── store/
│   │   │   ├── nodes/
│   │   │   └── index.tsx
│   │   └── vite.config.ts
│   │
│   └── docs/                      # Optional developer documentation site
│
├── libs/
│   ├── models/                    # Shared TS interfaces
│   ├── api-client/                # Shared API service logic
│   ├── shared-ui/                 # Shared design tokens + UI primitives
│   ├── utils/                     # Common utilities
│   └── state/                     # Shared stores or RxJS subjects
│
├── tools/                         # Build scripts, release utilities
│
├── .editorconfig
├── .eslint.json
├── .prettierrc
└── README.md


⸻

🧱 B. Angular Application Shell

The Angular shell is the orchestrator:
	•	Handles routing, authentication, and layout.
	•	Embeds the React Flow component inside the /studio route.
	•	Manages backend APIs, state, and theming.

B.1 Folder Layout

angular-shell/src/app/
├── core/                         # Global services, interceptors, guards
│   ├── app.config.ts
│   ├── app.routes.ts
│   ├── interceptors/
│   ├── services/
│   │   ├── api-client.service.ts
│   │   ├── websocket.service.ts
│   │   ├── theme.service.ts
│   │   └── workflow-store.service.ts
│   └── guards/
│
├── layout/                       # Topbar, sidebar, console
│   ├── layout.component.ts
│   ├── topbar/
│   │   ├── topbar.component.ts
│   │   └── command-palette.component.ts
│   ├── sidebar/
│   │   ├── sidebar.component.ts
│   │   └── sidebar-item.component.ts
│   └── console-panel/
│       └── console-panel.component.ts
│
├── modules/
│   ├── dashboard/
│   │   ├── dashboard.page.ts
│   │   ├── dashboard-card.component.ts
│   │   └── dashboard.routes.ts
│   ├── workflows/
│   │   ├── workflows.page.ts
│   │   ├── workflow-list.component.ts
│   │   └── workflows.routes.ts
│   ├── clients/
│   ├── executions/
│   ├── settings/
│   └── studio/
│       ├── studio.page.ts
│       ├── react-flow-wrapper.component.ts
│       └── studio.routes.ts
│
├── shared/                       # Shared Angular components
│   ├── button/
│   ├── modal/
│   ├── icons/
│   └── tooltip/
│
└── app.component.ts


⸻

B.2 Key Angular Components

Component	Purpose
layout.component.ts	Root layout, contains topbar + sidebar + router outlet
topbar.component.ts	Command palette, Run/Save, theme, status indicators
sidebar.component.ts	Navigation tabs (Workflows, Clients, Executions)
console-panel.component.ts	Bottom terminal/log viewer
studio.page.ts	Wrapper route for workflow builder
react-flow-wrapper.component.ts	Hosts <wbskt-react-flow> web component


⸻

B.3 Angular State

Using signals for lightweight, reactive global state:

// workflow-store.service.ts
export const currentWorkflow = signal<Workflow | null>(null);
export const connectionStatus = signal<'connected' | 'reconnecting' | 'offline'>('connected');
export const theme = signal<'dark' | 'light'>('dark');

These can be injected anywhere and synced with localStorage or backend.

⸻

B.4 Angular Routing

export const routes: Routes = [
  { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
  { path: 'dashboard', loadChildren: () => import('./modules/dashboard/dashboard.routes') },
  { path: 'workflows', loadChildren: () => import('./modules/workflows/workflows.routes') },
  { path: 'studio/:workflowId', loadChildren: () => import('./modules/studio/studio.routes') },
  { path: '**', redirectTo: 'dashboard' }
];


⸻

⚛️ C. React Flow Studio

The React Flow app lives independently under projects/react-flow-studio/.
It compiles to a Web Component that Angular loads dynamically.

C.1 Folder Structure

react-flow-studio/src/
├── components/
│   ├── WorkflowEditor.tsx
│   ├── WorkflowCanvas.tsx
│   ├── Toolbar.tsx
│   ├── NodeLibrary.tsx
│   ├── NodeInspector.tsx
│   ├── ConsolePanel.tsx
│   └── MiniMapOverlay.tsx
│
├── nodes/
│   ├── TriggerNode.tsx
│   ├── ActionNode.tsx
│   ├── ModifierNode.tsx
│   └── NodeTypes.ts
│
├── hooks/
│   ├── useWorkflowState.ts
│   ├── useSelection.ts
│   ├── useHotkeys.ts
│   └── useAutosave.ts
│
├── store/
│   └── workflowStore.ts
│
├── context/
│   ├── WorkflowContext.tsx
│   └── ThemeContext.tsx
│
├── types/
│   ├── Workflow.ts
│   ├── Node.ts
│   └── Connection.ts
│
├── utils/
│   ├── serialize.ts
│   ├── debounce.ts
│   └── validations.ts
│
├── index.tsx
├── main.tsx
└── web-component.tsx


⸻

C.2 React Component Hierarchy

<WorkflowEditor>
 ├── <Toolbar />
 ├── <div class="editor-layout">
 │     ├── <NodeLibrary />
 │     ├── <ReactFlowProvider>
 │     │     ├── <WorkflowCanvas />  ← actual canvas
 │     │     ├── <MiniMapOverlay />
 │     │     ├── <Controls /> (zoom, fit, etc.)
 │     │     └── <Background />
 │     └── <NodeInspector />
 └── <ConsolePanel />
</WorkflowEditor>

Each component is modular and lazy-loaded.
React Flow’s internal state is managed by Zustand to support undo/redo and time-travel debugging.

⸻

C.3 React → Angular Communication

React emits workflow updates using a CustomEvent:

window.dispatchEvent(new CustomEvent('updated', { detail: workflowJson }));

Angular wrapper listens for them:

@HostListener('updated', ['$event'])
onWorkflowUpdated(event: CustomEvent) {
  this.workflowService.save(event.detail);
}

Props ([workflow]) are passed down from Angular to React as serialized JSON.

⸻

C.4 Zustand Store

export const useWorkflowStore = create((set) => ({
  nodes: [],
  edges: [],
  setNodes: (nodes) => set({ nodes }),
  setEdges: (edges) => set({ edges }),
  selectedNode: null,
  setSelectedNode: (node) => set({ selectedNode: node }),
}));


⸻

🔗 D. Angular ↔ React Lifecycle

Flow:
	1.	Angular loads /studio/:id route.
	2.	Angular fetches workflow JSON → passes to <wbskt-react-flow [workflow]>.
	3.	User edits in React Flow → emits updated event.
	4.	Angular receives update → saves via API.
	5.	Logs shown in console panel below React view.

⸻

🎨 E. Shared Libraries

Folder	Purpose
libs/models/	TS interfaces (Workflow, Node, Client, Execution)
libs/api-client/	Typed HTTP services (using Angular HttpClient)
libs/shared-ui/	Design tokens, buttons, modals (shadcn-based)
libs/utils/	Cross-project helpers
libs/state/	RxJS subjects for cross-framework communication


⸻

🧰 F. Theming & Design Tokens

Both Angular and React share a Tailwind + CSS variable theme:

:root {
  --color-bg: #1E1E1E;
  --color-text: #ECECEC;
  --color-primary: #4A9EFF;
  --color-accent: #FFB347;
  --radius: 6px;
  --transition: 150ms ease;
}

Stored in libs/shared-ui/theme.css.

⸻

🗺️ G. Developer Workflow

Task	Command
Run Angular shell	ng serve angular-shell
Run React Flow	npm run dev --workspace=react-flow-studio
Build React Web Component	npm run build --workspace=react-flow-studio
Serve both together	Angular serves static React build under /studio/
Lint all	npm run lint
Test all	npm run test
Storybook	npm run storybook --workspace=react-flow-studio


⸻

✅ Result

You now have a clean, scalable monorepo setup where:
	•	Angular handles the full app experience.
	•	React Flow delivers a rich, isolated node-based editor.
	•	Both share consistent data models and styling.
	•	The UI looks, feels, and performs like a desktop IDE — in the browser.

⸻