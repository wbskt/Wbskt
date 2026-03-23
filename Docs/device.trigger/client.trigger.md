
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