# Future Architecture: On-Demand Hybrid Ping System

This document outlines the plan for a more efficient and scalable device latency measurement system. The current implementation (UI-initiated ping) is effective for an MVP but generates constant API traffic. The proposed "On-Demand" system will only generate ping traffic for devices that a user is actively monitoring in the UI.

## 1. The Problem
A UI-initiated ping every 5 seconds creates significant API load and mixes user network latency with system latency. A constant backend-initiated ping for all online devices creates massive, unnecessary load on the event bus and socket servers.

## 2. The Solution: "Subscribe-to-Ping"
The UI will explicitly tell the backend which devices it is currently observing. The backend will only initiate a ping loop for those specific devices.

### A. The "I'm Watching" Flow (UI -> Backend)
1.  **UI Connects**: The browser connects to the `NotificationHub` via SignalR.
2.  **UI Subscribes**: When a user opens the details tab for a specific client (e.g., `ESP32_LivingRoom`), the UI will call a new method on the `NotificationHub`:
    ```javascript
    await connection.invoke("SubscribeToDevicePings", "client-ref-id-123");
    ```
3.  **Backend Acknowledges**: The `NotificationHub` receives this request and publishes a new internal event: `UserSubscribedToDevicePings(string ClientRefId, string ConnectionId)`.

### B. The "Ping Loop" (Backend Service)
1.  **`PingManagerService`**: A new singleton or background service in `Management.Host`.
2.  **State Management**: This service will maintain an in-memory `ConcurrentDictionary<Guid, HashSet<string>>` that maps a `ClientRefId` to a set of SignalR `ConnectionId`s that are watching it.
3.  **Subscription Handling**: It listens for `UserSubscribedToDevicePings` and `UserUnsubscribedFromDevicePings` events to manage its internal dictionary.
4.  **The Loop**:
    *   The service runs a timer (e.g., every 5 seconds).
    *   It iterates through the keys (the `ClientRefId`s) of its dictionary.
    *   For each client being watched, it publishes a `DevicePingCommand`.

### C. The "I'm Done Watching" Flow (UI -> Backend)
1.  **UI Unsubscribes**: When the user closes the device tab or navigates away, the UI calls another new Hub method:
    ```javascript
    await connection.invoke("UnsubscribeFromDevicePings", "client-ref-id-123");
    ```
2.  **Backend Acknowledges**: The hub publishes a `UserUnsubscribedFromDevicePings(string ClientRefId, string ConnectionId)` event.
3.  **`PingManagerService`**: Removes the `ConnectionId` from the set for that client. If the set becomes empty, it stops sending pings for that device.

## 3. Benefits
*   **Efficiency**: Ping traffic is only generated for actively observed devices.
*   **Accuracy**: Latency is still measured purely on the backend, providing a clean system metric.
*   **Scalability**: The load scales with active UI sessions, not the total number of online devices.
*   **Resilience**: The `PingManagerService` can also handle SignalR's `OnDisconnectedAsync` event to automatically clean up subscriptions if a browser tab is closed unexpectedly.

This provides a clear path to a more sophisticated and resource-friendly latency measurement feature for a future release.
