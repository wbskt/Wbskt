# WBSKT Platform — UI Mockup Build Prompt

> **Paste this whole document into your UI mockup agent (Fable, etc.).**
> It describes a complete, high‑fidelity, **static/clickable HTML+CSS mockup** of the WBSKT
> platform. No backend, no real APIs, no live sockets — every screen uses realistic
> placeholder data and represents *states* rather than live behavior.

---

## 0. Role, goal, and output contract

You are a senior product designer + front‑end engineer. Produce a **high‑fidelity, clickable
HTML/CSS mockup** of the WBSKT platform: a marketing site, an auth flow, and an IDE‑style
console app. It must look like a real, shipping product — not a wireframe.

**Output format (important for later editability):**

- **Static HTML + CSS only.** Plain semantic HTML5. No build step required.
- **Tailwind via CDN is allowed**, but *also* define a single **`theme.css`** with **CSS
  custom properties (design tokens)** for all colors, radii, spacing, shadows, and fonts,
  so the entire look can be re‑skinned by editing one file.
- **One file per screen** (e.g. `console-clients.html`), plus shared partials for the
  header, left rail, explorer, and tab bar (repeat the markup in each file, but keep it
  **identical and clearly delimited** with `<!-- BEGIN shell:header --> … <!-- END -->`
  comments so a human can find and edit it).
- **Clickable navigation:** real `<a href>` links between screens so the reviewer can walk
  the whole product. Tabs, menus, and modals can be toggled with the tiniest amount of
  vanilla JS or the CSS `:target`/checkbox trick — keep JS minimal and readable.
- **No inline styles**, no frameworks beyond Tailwind CDN + your `theme.css`. No React/Vue.
- Provide an **`index.html`** that is a site map: a grid of thumbnails/links to every screen
  and every notable state (empty, populated, error, modal‑open).
- Use **placeholder data that looks real** (plausible device names, timestamps, PINs, JSON
  payloads, run IDs). Never show "Lorem ipsum".
- Icons: use an inline SVG icon set (Lucide/Phosphor style). Fonts: **Inter** (UI) +
  **JetBrains Mono** (code, IDs, payloads, PINs).

**Explicitly out of scope:** authentication logic, real websockets, real drag‑and‑drop
graph editing, data persistence, responsiveness below tablet width (desktop‑first; a
graceful tablet layout is a plus, mobile is not required for the console).

---

## 1. Design language

---

## 2. Product surface map (build all of these)

```
wbskt.com (marketing, LIGHT)            console.wbskt.com (app, DARK "Island")
├── Landing / hero                      ├── Workspaces list  (pre-workspace)
├── Features                            ├── Admin: Access Control (workspace/org)
├── Solutions                           │     ├── Users
├── Pricing                             │     ├── Groups / Teams  (nested)
├── Documentation (docs shell)          │     ├── Roles
├── About                               │     ├── Permissions catalog
└── Login / Sign up  ─────────────►     │     └── Permission Check simulator
                                        └── Workspace shell (IDE)
                                              ├── Clients
                                              ├── Policies
                                              ├── Workflows  (+ node editor, executions)
                                              ├── Audit Logs
                                              └── Integrations / Marketplace
```

---

## 3. Marketing site — `wbskt.com` (light, marketing aesthetic)

Not the IDE look — a modern, confident developer‑tool marketing site (think Vercel/Linear/
Postman landing pages). Sticky top nav: **logo + Features · Solutions · Pricing · Docs ·
About** and right‑aligned **Log in** / **Sign up** (accent) buttons.

Screens to produce:

1. **Landing** — hero with a one‑line positioning ("The real‑time automation platform for
   engineers — command & control hardware, SaaS, and AI from one workflow IDE"), a product
   screenshot of the console, logo cloud, three feature highlights, a code/SDK snippet
   block (C#, Python, JS tabs), a CTA band, and footer.
   Weave in the strategic positioning: **persistent WebSockets + SDKs**, **stateful
   presence/heartbeats**, **bi‑directional command & control**, **WASM sandboxed
   marketplace**, **compute/connection‑based pricing (no per‑task tax)**.
2. **Features** — sections for Clients/SDKs, Policies, the Workflow IDE, Marketplace/
   Integrations, Security (sandboxing), Observability.
3. **Solutions** — cards for IoT/Industrial, Smart Home, FinTech, DevOps/Self‑healing,
   AI‑augmented hardware. Each with a short "IF … THEN …" example.
4. **Pricing** — 3–4 tiers (Free / Pro / Team / Enterprise) emphasizing **per compute /
   per connection**, contrasted against "per‑task" competitors. Feature comparison table.
5. **Documentation** — a docs shell: left nav tree, center article, right "on this page"
   TOC. One representative article page (e.g. "Register your first client").
6. **About** — mission, team placeholder, values.
7. **Login** and **Sign up** — clean centered card, email/password, SSO buttons
   (Google/GitHub), "forgot password". Sign‑up has name/workspace fields. On submit the
   link goes to **Workspaces list** (mock — no auth).

---

## 4. Console shell — `console.wbskt.com` (the IDE frame)

Every console screen (except the pre‑workspace Workspaces list) shares this **shell**.
Build it once as `<!-- BEGIN shell:* -->` partials and reuse.

### 4.1 Header (top island, full width, slim)

- **Left:** breadcrumb that **starts at the workspace name** and reflects the selected
  entity path, e.g. `Acme Greenhouse ▸ Clients ▸ sensor‑A ▸ Live`. The workspace segment
  is a **workspace switcher** (dropdown listing other workspaces + "Manage workspaces" +
  "Create workspace").
- **Center (optional):** a slim command/search box ("Search clients, policies, workflows…"
  ⌘K) — Postman/Rider style.
- **Right:** environment/status hints, a **credits meter** (e.g. "8,420 credits"), a help
  "?" menu, and an **account avatar** with the user's photo. Avatar menu: profile, theme
  toggle (dark/light), Admin / Access Control (if admin), sign out.

### 4.2 Left activity rail (thin vertical island)

Icon‑only buttons, one per workspace entity explorer, with tooltip + active indicator:

- **Clients** (plug/hardware icon)
- **Policies** (shield/key icon)
- **Workflows** (nodes/graph icon)
- **Audit Logs** (list/history icon)
- **Integrations** (puzzle/marketplace icon)

Bottom of rail: **Admin / Access Control** (only shown for admins), Settings, and the
account avatar (can also live here). Selecting a rail icon swaps the **Explorer** panel.

### 4.3 Explorer (tool‑window island, next to the rail)

Exactly like the VSCode/Rider project explorer: a titled panel ("CLIENTS", "POLICIES", …)
with a toolbar (search, new +, collapse‑all, refresh), and a **tree** of items with
status decorations. Selecting a tree item opens it as a **tab** in the main area.
Collapsible; resizable handle (visual only). Include a filter box at the top.

### 4.4 Main area — tabbed pages (Postman/Rider tabs)

- Directly below the header: a **page/tab bar**. Each open entity is a **tab** with an icon,
  a label, a dirty‑dot for unsaved edits, and a **× close button**. Tabs are **horizontally
  scrollable** when they overflow, with chevron/overflow control. Support a "+" to open new.
  Show a pinned/active tab style. Middle‑click‑to‑close affordance is implied.
- Below the tab bar: **page content**, rendered per the selected entity (Sections 5–9).
- Include an **empty state** ("No tab open — pick something from the Explorer") screen.

### 4.5 Global elements

Design and show at least once: a **modal** (create workspace / create policy / register
client), **toasts** (success/error), **context menus** (right‑click on tree items: open,
rename, deprecate/revoke, duplicate), **confirmation dialogs** (deprecate workspace,
disable policy), **loading skeletons**, and an **empty‑state** pattern for each explorer.

---

## 5. Pre‑workspace: Workspaces list

The first screen after login (no shell yet, or a minimal shell). Show:

- A header "Your workspaces" + **Create workspace** (accent) button.
- A grid/list of **workspace cards**: name, short description, member avatars/count,
  entity counts (clients, workflows), created date, and role badge (Owner/Admin/Member).
- Per‑card menu: Open, Settings, **Deprecate** (with a confirm dialog; deprecated cards show
  a muted/archived state and a "Restore" action).
- A **Create Workspace modal**: name, description, region, template ("Blank" / "IoT
  starter"). On confirm → console workspace shell.
- Show a **deprecated/archived** section toggle.
- **Empty state** for a brand‑new account (illustration + "Create your first workspace").

---

## 6. Admin — Access Control (users, groups/teams, roles, permissions)

Only visible to admins. This is its own area (reachable from the avatar menu / rail). Build
a left sub‑nav: **Users · Groups/Teams · Roles · Permissions · Check Access**.

### 6.1 Access‑control model (drives the UI copy and the simulator)

Relationships to represent:

- **User → Group(s)**, **User → Role(s)**, **User → Permission(s)** (direct allow/deny)
- **Group → Group(s)** (nested groups), **Group → Role(s)**
- **Role → Permission(s)**

**Permission resolution — "Check Permission P for User U" (DENY‑wins, default‑deny):**

1. Does U have an **explicit DENY** for P (User→Permission: DENY)? → **DENY** (stop).
2. Else, does U have an **explicit ALLOW** for P (User→Permission: ALLOW)? → **ALLOW** (stop).
3. Else, **resolve all roles for U** = direct roles + roles via groups + roles via **nested**
   groups.
4. Among resolved roles: does **any role explicitly DENY** P? **YES or NONE** → **DENY**;
   **NO** (a role grants and none deny) → **ALLOW**.

Reproduce this as a **flowchart diagram** on the "Check Access" page (see 6.6). Use
green **ALLOW** pills and red **DENY** pills to match the source diagram.

### 6.2 Users

- Table: avatar, name, email, status (Active/Invited/Suspended), groups (chips), roles
  (chips), last active. Toolbar: search, filter, **Invite user**.
- **User detail** (drawer or page) with tabs: **Overview**, **Groups**, **Roles**,
  **Direct Permissions** (a list where each permission can be set Inherit / Allow / Deny),
  and **Effective Permissions** (computed, read‑only, showing *why*: "DENY via role
  `ops-oncall`" / "ALLOW direct").
- Invite user modal (email, initial role/group).

### 6.3 Groups / Teams

- List of groups with member counts and **nested‑group** indicators.
- **Group detail:** members (users), **child groups** (nesting tree), assigned roles,
  and an "effective roles" note explaining inheritance up the group tree.
- Create/edit group modal; add members; add child group.

### 6.4 Roles

- List of roles (e.g. `workspace-admin`, `workflow-editor`, `client-operator`,
  `ops-oncall`, `read-only`) with permission counts and member counts.
- **Role detail:** a permission matrix — each permission is **Allow / Deny / Unset**.
  Show which users/groups have this role.

### 6.5 Permissions catalog

- A catalog of granular permissions grouped by entity, e.g.:
  `clients.view/register/revoke/send`, `policies.view/create/approve/reject/disable`,
  `workflows.view/edit/publish/run/cancel`, `integrations.view/connect/manage`,
  `auditlogs.view`, `workspace.manage/deprecate`, `admin.access-control`.
- Show as a grouped, searchable list with descriptions.

### 6.6 Check Access (permission simulator) — showcase screen

- Inputs: pick a **User** and a **Permission** (+ optional workspace scope).
- Output: render the **resolution flowchart** with the actual path **highlighted** for the
  chosen inputs, ending in a big **ALLOW** or **DENY** verdict, plus a step list:
  "1. Explicit user DENY? No → 2. Explicit user ALLOW? No → 3. Roles resolved: `ops-oncall`
  (via group `oncall-team` ▸ nested `sre`) → 4. Any role DENY? Yes (`ops-oncall`) →
  **DENY**." This makes the model tangible and is a strong demo piece.

---

## 7. Entity: **Clients**

Clients are SDK/socket connections (hardware, IoT, apps, scripts, sites) registered to the
workspace. They appear in the **Clients explorer**.

**Explorer:** tree grouped by policy or type; each client shows a **status dot**
(green online / grey offline / amber reconnecting), name, and platform icon. Toolbar:
search, **Register client**, group‑by toggle.

**Client page (a tab)** — an IDE‑like split layout:

- **Header strip:** client name, ID (mono), status pill (**Online** / **Offline** /
  **Reconnecting**), **uptime** or **"last online 3m ago"**, **ping/latency** (e.g. 42 ms
  with a sparkline), signal strength, the policy it registered under, platform/SDK
  (e.g. "C# SDK v1.4 · linux‑arm64"). Actions: Send, Ping, Revoke, Rename.
- **Left/main — Send panel:** a **JSON payload editor** (mono, syntax‑highlighted look,
  line numbers, a template dropdown) with a **Send** button and target/topic selector.
- **Center/right — Live comms:** a **live in/out message stream** (chat‑like log) with
  direction arrows (▲ out / ▼ in), timestamps, payload preview, and expandable rows; a
  filter (in/out/all) and "follow tail" toggle. Show representative streaming rows.
- **Right/side — Capabilities & State:** panels for **Capabilities/Properties** (declared
  by the client), and **State variables** (current values, last‑updated), rendered as a
  key/value table with types. Include a "properties changed" mini‑timeline.
- **Register Client modal:** choose a **policy**, name, platform; show the resulting
  connection snippet + the policy PIN to paste into the SDK.
- **Empty state:** "No clients yet — register one to get a connection."

---

## 8. Entity: **Policies** (client enrollment policies)

A policy governs how clients enroll. **Explorer:** list of policies with mode + a
"3/10 used" capacity chip and enabled/disabled state.

**Create Policy modal:** **Name**, **enrollment mode** (**Auto‑enroll** vs
**On‑approval**), and **capacity** (**Limited N** vs **Unlimited**). On create, the system
mints a unique **PIN**.

**Policy page (a tab):**

- **PIN block:** the unique PIN shown **masked** (`•••• ••••`) with a **reveal on click**
  eye toggle + copy button. Explain: clients use this PIN to register under the policy.
- **Capacity:** if Limited, show **remaining slots** ("7 of 10 remaining") with a bar;
  if Unlimited, show ∞. Mode + enabled toggle.
- **Registered devices:** table of clients enrolled via this policy (name, ID, enrolled‑at,
  status) with a link to each client page.
- **Pending approval:** (only meaningful for On‑approval mode) queue of requests with
  **Approve / Reject** actions and device metadata.
- **Rejected / Revoked:** list with a **Revive/Restore** action.
- **Activity log (timeline):** chronological events — **policy created**, **device
  request**, **device approved**, **device rejected**, **device revived**, **policy
  disabled**, **limit exceeded** — each with icon, actor, timestamp, and detail.
- Header actions: **Disable policy** (confirm), **Regenerate PIN**, **Edit**.
- **Empty state** for a policy with no devices yet.

---

## 9. Entity: **Workflows** (the highlight — node editor + executions)

Two primary surfaces per workflow: the **node editor** and the **executions** view.
Ground the terminology in the WBSKT Workflow Engine V3 design (see companion doc). Node
families are **Trigger / Control / Action**.

**Explorer:** workflows list; each shows enabled/disabled, version (e.g. `v3`), and a
run‑health chip (e.g. "12 runs · 1 failed today").

### 9.1 Node editor (canvas) — represent as rich static states

Layout: **left node palette · center canvas · right inspector**, with a top toolbar and a
bottom minimap/zoom.

- **Top toolbar:** workflow name + version dropdown, **Enabled** toggle, **Save draft**,
  **Publish** (creates a new version), **Validate**, **Run/Test**, zoom controls, undo/redo,
  and a "Design ⇄ Executions" segmented switch.
- **Node palette (left):** searchable, grouped into **Triggers**, **Control**, **Actions**,
  and **Integrations** (marketplace nodes). Draggable‑looking cards (static). Examples:
  - **Triggers:** `Client Event`, `Schedule`, `Webhook`, `Signal`, `Event`, `Manual`.
  - **Control:** `Logic/Condition`, `ForEach`, `ParallelForEach`, `Join`, `Delay`,
    `Set Variable`, `Sub‑workflow`, `Wait for HTTP`, `Await Signal`.
  - **Actions:** `Client Message`, `Email`, `Webhook`, `Toast`, `Notify`, and marketplace
    actions (`Telegram: Send Photo`, `Twilio: Send SMS`, `OpenAI: Chat`, `Stripe: …`).
- **Canvas:** show a **realistic example graph** wired up (e.g. the greenhouse flow:
  `Client Trigger → Logic(temp>35) → OpenVent → Delay 10m → Logic → SMS`, with a
  ParallelForEach→Join fan‑out somewhere). Nodes are **island cards** with a colored family
  accent (Trigger/Control/Action), a title, an icon, and **named ports** (small labeled
  dots): e.g. Logic node has `true`/`false`; ForEach has `body`/`done`; wait nodes have
  `completed`/`timeout`. **Edges** are smooth bezier wires connecting ports; show **one edge
  mid‑draw** in a separate state. Show a **selected node** (accent ring) and a **node with a
  validation warning** (amber badge) and a **failed node** from a test run (red badge).
- **Inspector (right):** config for the selected node. Show several variants:
  - Trigger config: correlation key expression, **concurrency policy** (AllowParallel /
    Queue / CancelExisting / DropIfRunning), filter.
  - Action config: the action's fields + a **Retry policy** sub‑panel (max attempts,
    backoff None/Fixed/Linear/Exponential, jitter, retry‑on filter) + **On failure**
    (FailBranch / FailRun / Continue / Compensate).
  - An **expression field** style showing `$trigger.*`, `$output`, `$local`, `$shared`,
    `$run` autocomplete hints.
- **Shared Variables panel:** a declared, typed list (name, type Counter/Number/String/Bool/
  Json, default, reset policy) — e.g. `smsToday: Counter, default 0, DailyAtUtc(02:00)`.
- **Bottom bar:** minimap, zoom %, validation summary ("2 warnings"), credits‑per‑run
  estimate.
- **Publish confirm modal:** shows version bump `v3 → v4` and a soft‑warning list
  (e.g. "Trigger has no outbound edges").

### 9.2 Executions (runs) view

Segmented switch flips the main area to run history/queue for this workflow.

- **Runs table/list:** each run row = RunId (mono), trigger source, started/finished,
  duration, and a **status pill** using the engine's six statuses:
  **Completed** (green), **Failed** (red), **PartiallyFailed** (amber), **Cancelled**
  (grey), **Faulted** (red‑outline), **OutOfCredits** (amber‑outline). Filters by status,
  time range, correlation key. Show a **Queue** section (pending triggers under Queue
  policy) and a live/running run with a spinner.
- **Run detail (drawer/page):**
  - Summary: status, trigger payload (`$trigger`), version pinned, credits consumed,
    correlation key, active/failed/completed branch counts.
  - **Branch timeline / graph replay:** show the DAG of executed nodes with per‑node
    outcome coloring, plus a **history‑event log** (append‑only) listing events like
    `RunStarted, BranchStarted, NodeStarted, NodeCompleted, NodeAttemptFailed, NodeFailed,
    BookmarkCreated, BookmarkConsumed, BranchCompleted, RunCompleted` with severity
    (Info/Debug/Warn/Error), timestamps, and node refs.
  - **State/properties:** run vars (`$run`), local vars per branch (`$local`), and shared
    vars snapshot (`$shared`).
  - **Logs tab:** structured log lines `{RunId, BranchId, NodeId, NodeKind, Attempt,
    Outcome, DurationMs}`.
  - Actions: **Cancel run** (if running), **Re‑run** (new trigger), copy RunId.
- **Empty state:** "No runs yet — publish and trigger this workflow."

---

## 10. Entity: **Audit Logs**

Workspace‑wide audit trail. A filterable **table/timeline**: timestamp, actor
(user/avatar), action, target entity (client/policy/workflow/integration/user), result,
and an expandable detail (before/after or payload). Filters: entity type, actor, action,
date range, free‑text search. Export button (visual). Include a **timeline** toggle and a
representative dense dataset (policy approvals, client revokes, workflow publishes, role
changes, integration connects).

---

## 11. Entity: **Integrations / Marketplace** (Virtual Clients & Marketplace Nodes)

Integrations are **Virtual Clients** — software bridges to 3rd‑party services. Each
marketplace entry provides **Trigger nodes (telemetry)** and **Action nodes (commands)**
that appear in the Workflow palette.

Build these views:

- **Marketplace browse:** searchable grid of integration packs with icon/branding,
  category, short description, and a "Connected" badge if added. Categories:
  **Communication & Social** (Telegram, Discord), **SMS/Email/Voice** (Twilio, SendGrid),
  **Oracles** (OpenWeatherMap, AccuWeather, Coinbase, Yahoo Finance), **Productivity/SaaS**
  (Google Workspace, Microsoft 365, Notion, Airtable), **Enterprise/FinTech** (Stripe,
  PayPal, Shopify, WooCommerce), **AI/Logic** (OpenAI, Anthropic, Azure Vision, DeepStack),
  **Infra/DevOps** (AWS, Azure, Cloudflare).
- **Integration detail page:** hero (icon, name, publisher, install count), tabs:
  **Overview**, **Nodes** (list of Trigger nodes e.g. `Discord: On Message`, `Telegram:
  Send Photo`; and Action nodes e.g. `Twilio: Send SMS`), **Configuration/Credentials**
  (the config schema — API key fields, OAuth "Connect Google account" button, etc., with a
  security note about WASM/sandbox isolation), **Documentation** (auto‑generated inputs/
  outputs), and **Use cases** (the "IF … THEN …" examples).
- **Connected integrations (manage):** list of the workspace's connected integrations with
  status (Connected/Needs attention/Expired token), credential masking (reveal/copy),
  **Add credential**, **Reconnect**, **Remove** actions, and a per‑integration usage note
  ("used in 3 workflows").
- **Add integration flow (modal/wizard):** select service → enter/authorize credentials →
  name the virtual client → confirm; result shows the new nodes now available in Workflows.
- **Empty state:** "No integrations yet — browse the Marketplace."

---

## 12. Navigation / clickability map (wire these links)

- Marketing `Log in`/`Sign up` → **Login/Sign up** → **Workspaces list**.
- Workspace card **Open** → **Console shell** (default to Clients explorer + empty main).
- Left rail icons → swap **Explorer** (Clients/Policies/Workflows/Audit/Integrations).
- Explorer item click → opens a **tab** with that entity's page.
- Workflow tab → **Design ⇄ Executions** switch; run row → **Run detail**.
- Policy page → Approve/Reject move rows between Pending/Registered/Rejected states
  (show as separate static states or a tiny JS toggle).
- Avatar menu → **Admin / Access Control** → sub‑nav (Users/Groups/Roles/Permissions/Check
  Access); Check Access renders the highlighted flowchart verdict.
- Breadcrumb workspace segment → **workspace switcher** dropdown.
- Every screen reachable from **`index.html`** site map.

