# WebSocket System API Documentation

## Overview

This API provides endpoints for managing a WebSocket-based IoT device communication system. The system supports user authentication, channel management, publisher management, client enrollment, and payload dispatching.

## Base URL

```
https://your-server.com
```

## Authentication

The API uses Bearer token authentication. Include the token in the Authorization header:

```
Authorization: Bearer <your-token>
```

## Endpoints

---

## User Management

### User Registration

**POST** `/api/users/register`

Register a new user account.

**Request Body:**
```json
{
  "emailId": "user@example.com",
  "password": "securepassword",
  "userName": "John Doe"
}
```

**Response:**
```json
{
  "message": "User created. Please login"
}
```

**Notes:**
- `userName` is optional. If not provided, `emailId` will be used as the username
- Email uniqueness validation is planned for future implementation

### User Login

**POST** `/api/users/login`

Authenticate a user and receive an access token.

**Request Body:**
```json
{
  "emailId": "user@example.com",
  "password": "securepassword"
}
```

**Response:**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

**Error Responses:**
- `401 Unauthorized` - User does not exist or credentials are incorrect

---

## Enrollment Policies

### Create Enrollment Policy

**POST** `/api/enroll/code`

Create a new enrollment policy for IoT device registration.

**Headers:**
```
Authorization: Bearer <token>
Content-Type: application/json
```

**Request Body:**
```json
{
  "name": "Factory IoT Network",
  "policyType": 2,
  "maxClients": 500
}
```

**Policy Types:**
- `1` - Time Limited (requires `expiryDate`)
- `2` - Number of Clients (requires `maxClients`)
- `3` - Unlimited (no restrictions)
- `4` - Time and Count (requires both `expiryDate` and `maxClients`)

**Example Requests:**

**Time Limited Policy:**
```json
{
  "name": "30-Day Trial",
  "policyType": 1,
  "expiryDate": "2025-02-15T00:00:00Z"
}
```

**Number of Clients Policy:**
```json
{
  "name": "Factory Floor Sensors",
  "policyType": 2,
  "maxClients": 100
}
```

**Unlimited Policy:**
```json
{
  "name": "Production Network",
  "policyType": 3
}
```

**Time and Count Policy:**
```json
{
  "name": "Conference Demo",
  "policyType": 4,
  "expiryDate": "2025-01-16T18:00:00Z",
  "maxClients": 50
}
```

**Response:**
```json
{
  "policyRef": "550e8400-e29b-41d4-a716-446655440000",
  "name": "Factory IoT Network",
  "policyType": 2,
  "maxClients": 500,
  "expiryDate": null,
  "currentUsage": 0,
}
```

### Get User's Policies

**GET** `/api/enroll/policies`

Retrieve all enrollment policies for the authenticated user.

**Headers:**
```
Authorization: Bearer <token>
```

**Response:**
```json
[
  {
    "policyRef": "550e8400-e29b-41d4-a716-446655440000",
    "name": "Factory IoT Network",
    "policyType": 2,
    "maxClients": 500,
    "expiryDate": null,
    "currentUsage": 15,
  }
]
```

---

## Client Management

### Enroll IoT Device

**POST** `/api/clients/{policyRef}/enroll`

Register an IoT device using an enrollment policy.

**Request Body:**
```json
{
  "name": "Temperature Sensor 001",
  "uniqueRef": "660e8400-e29b-41d4-a716-446655440000"
}
```

**Response:**
```json
{
  "message": "Enrollment successful",
  "clientId": 123,
  "policyUserId": 456
}
```

**Error Responses:**
- `400 Bad Request` - Invalid or expired enrollment policy
- `500 Internal Server Error` - Server error during enrollment

### Get User's Clients

**GET** `/api/clients`

Retrieve all clients for the authenticated user.

**Headers:**
```
Authorization: Bearer <token>
```

**Response:**
```json
[
  {
    "name": "Temperature Sensor 001",
    "uniqueRef": "660e8400-e29b-41d4-a716-446655440000",
  }
]
```

### Request Connection

**POST** `/api/clients/connect`

Request a WebSocket connection (placeholder endpoint).

**Response:**
```json
{
  "message": "Connection request received"
}
```

---

## Channel Management

### Get All Channels

**GET** `/api/channels`

Retrieve all channels for the authenticated user.

**Headers:**
```
Authorization: Bearer <token>
```

**Response:**
```json
[
  {
    "channelRef": "770e8400-e29b-41d4-a716-446655440000",
    "name": "Temperature Data",
    "description": "Channel for temperature sensor data"
  }
]
```

### Create Channel

**POST** `/api/channels`

Create a new channel.

**Headers:**
```
Authorization: Bearer <token>
Content-Type: application/json
```

**Request Body:**
```json
{
  "channelRef": "770e8400-e29b-41d4-a716-446655440000",
  "name": "Temperature Data",
  "description": "Channel for temperature sensor data"
}
```

**Response:**
```json
{
  "channelRef": "770e8400-e29b-41d4-a716-446655440000",
  "name": "Temperature Data",
  "description": "Channel for temperature sensor data"
}
```

### Get Publishers for Channel

**GET** `/api/channels/{channelRef}/publishers`

Retrieve all publishers associated with a specific channel.

**Headers:**
```
Authorization: Bearer <token>
```

**Response:**
```json
[
  {
    "publisherRef": "880e8400-e29b-41d4-a716-446655440000",
    "name": "Temperature Publisher",
    "description": "Publishes temperature data"
  }
]
```

### Add Publishers to Channel

**POST** `/api/channels/{channelRef}/publishers`

Add publishers to a channel.

**Headers:**
```
Authorization: Bearer <token>
Content-Type: application/json
```

**Request Body:**
```json
[
  "880e8400-e29b-41d4-a716-446655440000",
  "990e8400-e29b-41d4-a716-446655440000"
]
```

**Response:**
```json
{
  "message": "Publishers added successfully"
}
```

**Error Responses:**
- `400 Bad Request` - Could not add publishers to the given channel

### Remove Publishers from Channel

**DELETE** `/api/channels/{channelRef}/publishers`

Remove publishers from a channel.

**Headers:**
```
Authorization: Bearer <token>
Content-Type: application/json
```

**Request Body:**
```json
[
  "880e8400-e29b-41d4-a716-446655440000"
]
```

**Response:**
```json
{
  "message": "Publishers removed successfully"
}
```

**Error Responses:**
- `400 Bad Request` - Could not remove publishers from channel

### Get Publishers for Multiple Channels

**POST** `/api/channels/publishers`

Retrieve publishers for multiple channels.

**Headers:**
```
Authorization: Bearer <token>
Content-Type: application/json
```

**Request Body:**
```json
[
  "770e8400-e29b-41d4-a716-446655440000",
  "880e8400-e29b-41d4-a716-446655440000"
]
```

**Response:**
```json
[
  {
    "channelRef": "770e8400-e29b-41d4-a716-446655440000",
    "publishers": [
      {
        "publisherRef": "880e8400-e29b-41d4-a716-446655440000",
        "name": "Temperature Publisher",
        "description": "Publishes temperature data"
      }
    ]
  }
]
```

---

## Publisher Management

### Get All Publishers

**GET** `/api/publishers`

Retrieve all publishers for the authenticated user.

**Headers:**
```
Authorization: Bearer <token>
```

**Response:**
```json
[
  {
    "publisherRef": "880e8400-e29b-41d4-a716-446655440000",
    "name": "Temperature Publisher",
    "description": "Publishes temperature data"
  }
]
```

### Create Publisher

**POST** `/api/publishers`

Create a new publisher.

**Headers:**
```
Authorization: Bearer <token>
Content-Type: application/json
```

**Request Body:**
```json
{
  "publisherRef": "880e8400-e29b-41d4-a716-446655440000",
  "name": "Temperature Publisher",
  "description": "Publishes temperature data"
}
```

**Response:**
```json
{
  "publisherRef": "880e8400-e29b-41d4-a716-446655440000",
  "name": "Temperature Publisher",
  "description": "Publishes temperature data"
}
```

### Get Channels for Publisher

**GET** `/api/publishers/{publisherRef}/channels`

Retrieve all channels associated with a specific publisher.

**Headers:**
```
Authorization: Bearer <token>
```

**Response:**
```json
[
  {
    "channelRef": "770e8400-e29b-41d4-a716-446655440000",
    "name": "Temperature Data",
    "description": "Channel for temperature sensor data"
  }
]
```

### Add Channels to Publisher

**POST** `/api/publishers/{publisherRef}/channels`

Add channels to a publisher.

**Headers:**
```
Authorization: Bearer <token>
Content-Type: application/json
```

**Request Body:**
```json
[
  "770e8400-e29b-41d4-a716-446655440000",
  "880e8400-e29b-41d4-a716-446655440000"
]
```

**Response:**
```json
{
  "message": "Channels added successfully"
}
```

**Error Responses:**
- `400 Bad Request` - Could not add channels to the given publisher

### Remove Channels from Publisher

**DELETE** `/api/publishers/{publisherRef}/channels`

Remove channels from a publisher.

**Headers:**
```
Authorization: Bearer <token>
Content-Type: application/json
```

**Request Body:**
```json
[
  "770e8400-e29b-41d4-a716-446655440000"
]
```

**Response:**
```json
{
  "message": "Channels removed successfully"
}
```

**Error Responses:**
- `400 Bad Request` - Could not remove channels from publisher

### Get Channels for Multiple Publishers

**POST** `/api/publishers/channels`

Retrieve channels for multiple publishers.

**Headers:**
```
Authorization: Bearer <token>
Content-Type: application/json
```

**Request Body:**
```json
[
  "880e8400-e29b-41d4-a716-446655440000",
  "990e8400-e29b-41d4-a716-446655440000"
]
```

**Response:**
```json
[
  {
    "publisherRef": "880e8400-e29b-41d4-a716-446655440000",
    "channels": [
      {
        "channelRef": "770e8400-e29b-41d4-a716-446655440000",
        "name": "Temperature Data",
        "description": "Channel for temperature sensor data"
      }
    ]
  }
]
```

---

## Payload Dispatching

### Dispatch Payload (GET)

**GET** `/api/payloads/{publisherId}/dispatch`

Dispatch a payload to a specific publisher using GET method.

**Headers:**
```
Authorization: Bearer <token>
```

**Response:**
```json
{
  "message": "Payload dispatched successfully"
}
```

**Error Responses:**
- `400 Bad Request` - No channels with the specified publisher ID

### Dispatch Payload (POST)

**POST** `/api/payloads/dispatch`

Dispatch a payload with custom data.

**Headers:**
```
Authorization: Bearer <token>
Content-Type: application/json
```

**Request Body:**
```json
{
  "publisherRef": "880e8400-e29b-41d4-a716-446655440000",
  "data": "{\"temperature\": 25.5, \"humidity\": 60}",
  "ensureDelivery": true
}
```

**Response:**
```json
{
  "message": "Payload dispatched successfully"
}
```

**Error Responses:**
- `400 Bad Request` - No channels with the specified publisher ID

---

## Health and WebSocket

### Health Check

**GET** `/`

Simple health check endpoint.

**Response:**
```json
{
  "payloadId": "00000000-0000-0000-0000-000000000000",
  "publisherRef": "00000000-0000-0000-0000-000000000000",
  "data": "",
  "ensureDelivery": false,
  "channelRef": "00000000-0000-0000-0000-000000000000"
}
```

### WebSocket Connection

**GET** `/ws`

Establish a WebSocket connection (requires socket server authentication).

**Headers:**
```
Authorization: Bearer <socket-server-token>
```

**Notes:**
- This endpoint is for WebSocket server connections
- Requires special socket server authentication
- Used for real-time communication with IoT devices

---

## Error Responses

### Standard Error Format

```json
{
  "error": "Error description"
}
```

### Common HTTP Status Codes

- `200 OK` - Request successful
- `400 Bad Request` - Invalid request data
- `401 Unauthorized` - Authentication required or invalid credentials
- `404 Not Found` - Resource not found
- `500 Internal Server Error` - Server error

---

## Data Models

### Enrollment Policy Types

| Type | Name | Description | Required Fields |
|------|------|-------------|-----------------|
| 1 | TimeLimited | Expires after specified date | `expiryDate` |
| 2 | NumberOfClients | Limited by client count | `maxClients` |
| 3 | Unlimited | No restrictions | None |
| 4 | TimeAndCount | Both time and count limits | `expiryDate`, `maxClients` |

### Authentication Schemes

- `UserScheme` - For user authentication
- `SocketServerScheme` - For WebSocket server authentication

---

## Rate Limiting

Currently, no rate limiting is implemented. Consider implementing rate limiting for production use.

## Security Considerations

1. **Authentication**: All endpoints (except health check) require authentication
2. **Authorization**: Users can only access their own resources
3. **Input Validation**: All inputs are validated server-side
4. **HTTPS**: Use HTTPS in production for secure communication
5. **Token Security**: Store tokens securely and implement proper token rotation

---

## IoT Device Integration

### Typical IoT Device Flow

1. **Enrollment**: Device uses enrollment policy to register
2. **Authentication**: Device authenticates using client credentials
3. **Connection**: Device establishes WebSocket connection
4. **Data Publishing**: Device publishes data to channels
5. **Data Consumption**: Other devices/clients consume data from channels

### Example IoT Device Integration

```javascript
// 1. Enroll device
const enrollmentResponse = await fetch('/api/clients/{policyRef}/enroll', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({
    name: 'Temperature Sensor 001',
    uniqueRef: '660e8400-e29b-41d4-a716-446655440000'
  })
});

// 2. Connect to WebSocket
const ws = new WebSocket('wss://your-server.com/ws');
ws.onopen = () => {
  console.log('Connected to WebSocket server');
};

// 3. Publish data
ws.send(JSON.stringify({
  publisherRef: '880e8400-e29b-41d4-a716-446655440000',
  data: JSON.stringify({ temperature: 25.5, humidity: 60 })
}));
``` 