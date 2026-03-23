The core idea: the schema describes **visual structure** (ports, layout, inputs) completely separately from **runtime data** (user's configured values).

```json
{
  "$schema": "https://wbskt.io/schemas/node-definition/v1",
  "type": "trigger:device",
  "meta": {
    "label": "Device Trigger",
    "description": "Fires when a device emits telemetry or a property changes.",
    "category": "trigger",
    "icon": "bolt",
    "color": {
      "header": "#C05A1F",
      "border": "#E07030",
      "badge": "LIVE"
    },
    "tags": ["hardware", "realtime", "esp32"]
  },

  "ports": {
    "inputs": [],
    "outputs": [
      {
        "id": "on_telemetry",
        "label": "On Telemetry",
        "description": "Fires on every data packet",
        "type": "flow",
        "position": "right",
        "dot": { "color": "#E07030" }
      },
      {
        "id": "on_property_change",
        "label": "On Property Change",
        "description": "Triggers on specific attribute updates",
        "type": "flow",
        "position": "right",
        "dot": { "color": "#E07030" }
      }
    ]
  },

  "inputs": [
    {
      "id": "target_device",
      "label": "Target Device",
      "type": "select:client",
      "clientFilter": "native",
      "placeholder": "Select a device...",
      "required": true,
      "icon": "chip"
    }
  ],

  "context_output": {
    "description": "Variables this node injects into $context for downstream nodes",
    "fields": [
      { "key": "deviceId",   "type": "string" },
      { "key": "temp",       "type": "number", "example": 31.5 },
      { "key": "timestamp",  "type": "datetime" },
      { "key": "payload",    "type": "object"  }
    ]
  },

  "badges": [
    { "id": "live_indicator", "type": "live_dot", "position": "header_right" }
  ]
}
```

Now the full schema across all your node types, showing every `input.type` you'll need:

```json
{
  "$schema": "https://wbskt.io/schemas/node-definition/v1",
  "type": "control:logic",
  "meta": {
    "label": "Logic Gate",
    "category": "control",
    "icon": "git-branch",
    "color": { "header": "#1A2A4A", "border": "#3A6ABF" }
  },

  "ports": {
    "inputs": [
      {
        "id": "flow_in",
        "label": "In",
        "type": "flow",
        "position": "left",
        "dot": { "color": "#4A90D9" }
      }
    ],
    "outputs": [
      {
        "id": "match",
        "label": "Match",
        "description": "Executes if true",
        "type": "flow",
        "position": "right",
        "dot": { "color": "#4CAF50" }
      },
      {
        "id": "otherwise",
        "label": "Otherwise",
        "description": "Executes if false",
        "type": "flow",
        "position": "right",
        "dot": { "color": "#888888" }
      }
    ]
  },

  "inputs": [
    {
      "id": "lhs",
      "label": "Subject (LHS)",
      "type": "expression",
      "placeholder": "$context.temp",
      "supportsContextPicker": true,
      "required": true
    },
    {
      "id": "operator",
      "label": "Operator",
      "type": "select:static",
      "layout": "inline_left",
      "options": [
        { "value": "eq",  "label": "==" },
        { "value": "neq", "label": "!=" },
        { "value": "gt",  "label": ">"  },
        { "value": "gte", "label": ">=" },
        { "value": "lt",  "label": "<"  },
        { "value": "lte", "label": "<=" },
        { "value": "contains", "label": "contains" },
        { "value": "exists",   "label": "exists"   }
      ],
      "required": true
    },
    {
      "id": "rhs",
      "label": "Value (RHS)",
      "type": "text",
      "layout": "inline_right",
      "placeholder": "30",
      "supportsContextPicker": true,
      "required": true
    }
  ]
}
```

```json
{
  "$schema": "https://wbskt.io/schemas/node-definition/v1",
  "type": "action:command",
  "meta": {
    "label": "Send Command",
    "category": "action",
    "icon": "send",
    "color": { "header": "#1A3A2A", "border": "#2ECC71" }
  },

  "flags": [
    {
      "id": "trans_id",
      "label": "Trans-ID",
      "type": "toggle_badge",
      "position": "header_right",
      "default": true
    }
  ],

  "ports": {
    "inputs": [
      {
        "id": "flow_in",
        "label": "In",
        "type": "flow",
        "position": "left",
        "dot": { "color": "#2ECC71" }
      }
    ],
    "outputs": []
  },

  "inputs": [
    {
      "id": "target_client",
      "label": "Target Client",
      "type": "select:client",
      "clientFilter": "all",
      "placeholder": "Select target...",
      "icon": "target",
      "required": true
    },
    {
      "id": "concurrency_policy",
      "label": "Concurrency Policy",
      "type": "select:static",
      "icon": "layers",
      "options": [
        { "value": "allow_parallel", "label": "Allow Parallel" },
        { "value": "queue",          "label": "Queue"          },
        { "value": "drop",           "label": "Drop if busy"   },
        { "value": "cancel_prev",    "label": "Cancel previous"}
      ],
      "default": "allow_parallel"
    },
    {
      "id": "payload",
      "label": "JSON Payload",
      "type": "json_editor",
      "supportsTemplate": true,
      "templateHint": "Use $context.value to inject data from previous nodes.",
      "default": "{ \"cmd\": \"\" }",
      "required": true
    },
    {
      "id": "wait_for_ack",
      "label": "Wait for ACK",
      "type": "toggle",
      "default": false
    },
    {
      "id": "execution_queue",
      "label": "Execution Queue",
      "type": "queue_status",
      "readonly": true,
      "badge": { "field": "in_flight", "color": "#2ECC71" }
    }
  ],

  "footer": {
    "label": "No output · Auto Finish",
    "style": "muted"
  }
}
```

Now the full **input type catalogue** - this is what your registry needs to support:

```json
{
  "input_types": {

    "text": {
      "description": "Single-line string input",
      "props": ["placeholder", "maxLength", "supportsContextPicker"]
    },

    "number": {
      "description": "Numeric input with optional min/max/step",
      "props": ["placeholder", "min", "max", "step", "unit"]
    },

    "toggle": {
      "description": "Boolean on/off switch",
      "props": ["default"]
    },

    "toggle_badge": {
      "description": "Header-level flag badge with toggle (like your TRANS-ID: ENABLED)",
      "props": ["default", "position", "labelOn", "labelOff"]
    },

    "select:static": {
      "description": "Dropdown from a hardcoded options array",
      "props": ["options[]{value, label, icon?}", "default", "layout"]
    },

    "select:client": {
      "description": "Dropdown populated at runtime from the client registry",
      "props": ["clientFilter: native|virtual|all", "placeholder"]
    },

    "select:dynamic": {
      "description": "Dropdown populated by a registry endpoint at design time",
      "props": ["endpoint", "labelField", "valueField", "dependsOn"]
    },

    "expression": {
      "description": "Single-line expression input with $context variable autocomplete",
      "props": ["placeholder", "supportsContextPicker"]
    },

    "json_editor": {
      "description": "Multi-line JSON editor with syntax highlighting",
      "props": ["default", "supportsTemplate", "templateHint"]
    },

    "code_editor": {
      "description": "Multi-line code editor (for script nodes, webhook bodies etc.)",
      "props": ["language: js|python|powershell|bash", "default"]
    },

    "datetime_picker": {
      "description": "Date + time picker for scheduler nodes",
      "props": ["includeTimezone", "default"]
    },

    "cron_editor": {
      "description": "Visual cron expression builder",
      "props": ["default"]
    },

    "duration": {
      "description": "Duration input with unit selector (ms / s / min / hr)",
      "props": ["min", "max", "defaultUnit", "default"]
    },

    "key_value_list": {
      "description": "Repeating key=value row editor (e.g. HTTP headers)",
      "props": ["keyPlaceholder", "valuePlaceholder", "supportsContextPicker"]
    },

    "queue_status": {
      "description": "Read-only runtime display showing live queue depth",
      "props": ["readonly: true", "badge{field, color}"]
    },

    "section_header": {
      "description": "Visual divider / label inside the node panel - not a real input",
      "props": ["label", "icon"]
    }
  }
}
```

And the **port type catalogue**:

```json
{
  "port_types": {
    "flow":   "Standard execution edge - connects node A's output to node B's input",
    "data":   "Typed data edge - carries a value of a declared type (for future typed graph validation)",
    "error":  "Error branch - fired when the node throws, allows inline error handling"
  }
}
```

Finally, the **`layout` field** on inputs controls how the UI groups them:

```json
{
  "layout_values": {
    "full":         "Default. Input takes the full width of the panel.",
    "inline_left":  "Takes ~55% width, paired with inline_right neighbor",
    "inline_right": "Takes ~40% width, sits beside inline_left",
    "compact":      "Half width, two per row, for dense flag-style inputs"
  }
}
```

---

A few design decisions worth calling out:

**`context_output` on trigger nodes** is what allows your IDE to power `$context.` autocomplete in downstream `expression` and `json_editor` inputs. The registry ships the schema of what each node *produces*, and the UI merges upstream outputs into the picker. This is how your `$context.temp` in the Logic Gate knows what properties are available from `ESP32_LivingRoom`.

**`dependsOn` in `select:dynamic`** enables cascading dropdowns - e.g., selecting a workspace first narrows the client list. The UI re-fetches the endpoint whenever the parent field changes.

**`flags` vs `inputs`** are kept separate intentionally. Flags are header-level toggles (Trans-ID, Live indicator) that affect node *behavior metadata*, not the node's form layout. They render in the title bar, not in the body.

**`error` port** - you don't have this yet based on the screenshot, but you'll want it. When `Send Command` times out waiting for ACK, where does the flow go? An error port is the node-RED pattern you'll need once workflows hit production.

