# Enrollment Policies Feature

## Overview

The enrollment policies feature allows users to create enrollment policies that can be used to register client connections. Each policy generates a unique GUID reference and supports three different types of limitations.

## Policy Types

### 1. Time Limited
- **Description**: Policies that expire after a specified date/time
- **Required Fields**: `name`, `policyType: 1`, `expiryDate`
- **Usage**: Unlimited until expiry date is reached
- **Example**: A policy that expires on December 31, 2025

### 2. Number of Clients
- **Description**: Policies limited by the number of clients that can register
- **Required Fields**: `name`, `policyType: 2`, `maxClients`
- **Usage**: Limited to the specified number of client registrations
- **Example**: A policy that allows up to 10 clients to register

### 3. Single Use
- **Description**: Policies that can only be used once
- **Required Fields**: `name`, `policyType: 3`
- **Usage**: Limited to exactly 1 client registration
- **Example**: A policy that allows only one client to register

## API Endpoints

### Create Enrollment Policy
```
POST /api/enroll/code
Authorization: Bearer <token>
Content-Type: application/json

{
  "policyRef": "550e8400-e29b-41d4-a716-446655440000",
  "name": "My Policy",
  "policyType": 1,
  "expiryDate": "2025-12-31T23:59:59Z"
}
```

**Response:**
```json
{
  "id": 1,
  "userId": 123,
  "policyRef": "550e8400-e29b-41d4-a716-446655440000",
  "name": "My Policy",
  "policyType": 1,
  "maxClients": null,
  "expiryDate": "2025-12-31T23:59:59Z",
  "currentUsage": 0,
  "isActive": true,
  "lastModified": "2025-01-15T10:30:00Z"
}
```

**Note**: If `policyRef` is not provided or is empty, a new GUID will be generated automatically.

### Get User's Policies
```
GET /api/enroll/policies
Authorization: Bearer <token>
```

**Response:**
```json
[
  {
    "id": 1,
    "userId": 123,
    "policyRef": "550e8400-e29b-41d4-a716-446655440000",
    "name": "My Policy",
    "policyType": 1,
    "maxClients": null,
    "expiryDate": "2025-12-31T23:59:59Z",
    "currentUsage": 0,
    "isActive": true,
    "lastModified": "2025-01-15T10:30:00Z"
  }
]
```

### Enroll Client Using Policy
```
POST /api/clients/{policyRef}/enroll
Authorization: Bearer <token>
Content-Type: application/json

{
  "name": "My Client",
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

## Database Schema

### EnrollmentPolicies Table
```sql
CREATE TABLE [dbo].[EnrollmentPolicies] (
    [Id]                INT                 IDENTITY (1, 1) NOT NULL,
    [UserId]            INT                 NOT NULL,
    [PolicyRef]         UNIQUEIDENTIFIER    NOT NULL,
    [Name]              VARCHAR (100)       NOT NULL,
    [PolicyType]        INT                 NOT NULL, -- 1: TimeLimited, 2: NumberOfClients, 3: SingleUse
    [MaxClients]        INT                 NULL,     -- For NumberOfClients and SingleUse policies
    [ExpiryDate]        DATETIME            NULL,     -- For TimeLimited policies
    [CurrentUsage]      INT                 NOT NULL DEFAULT 0,
    [IsActive]          BIT                 NOT NULL DEFAULT 1,
    [LastModified]      DATETIME            DEFAULT CURRENT_TIMESTAMP
);
```

## Stored Procedures

- `EnrollmentPolicies_Insert` - Creates new policy with provided GUID
- `EnrollmentPolicies_GetAll` - Gets all policies with last modified filter
- `EnrollmentPolicies_IncrementUsage` - Increments usage and deactivates if needed
- `EnrollmentPolicies_CleanupExpired` - Deactivates expired time-limited policies

**Note**: All filtering (by user ID, policy reference, etc.) is performed in memory using the cached data from `GetAll`.

## Usage Examples

### Creating a Time-Limited Policy
```json
{
  "name": "Conference Access",
  "policyType": 1,
  "expiryDate": "2025-01-15T18:00:00Z"
}
```

### Creating a Multi-Client Policy
```json
{
  "name": "Team Access",
  "policyType": 2,
  "maxClients": 5
}
```

### Creating a Single-Use Policy
```json
{
  "name": "One-time Access",
  "policyType": 3
}
```

### Creating a Policy with Custom GUID
```json
{
  "policyRef": "550e8400-e29b-41d4-a716-446655440000",
  "name": "Custom Policy",
  "policyType": 2,
  "maxClients": 10
}
```

### Enrolling a Client
```json
{
  "name": "Conference Client",
  "uniqueRef": "660e8400-e29b-41d4-a716-446655440000"
}
```

## Validation Rules

1. **Time Limited**: Must have future expiry date
2. **Number of Clients**: Must have maxClients > 0
3. **Single Use**: Automatically sets maxClients to 1
4. **GUID Generation**: Automatically generates unique GUID references if not provided
5. **Usage Tracking**: Automatically increments usage and deactivates when limits are reached
6. **Client Registration**: Automatically assigns the client to the policy owner's user account

## Architecture

### Data Access Pattern
- **Single Source**: Only `EnrollmentPolicies_GetAll` stored procedure is used
- **In-Memory Filtering**: All filtering operations are performed on cached data
- **Real-time Updates**: SQL dependency ensures cache stays synchronized
- **Performance**: Reduces database round trips and simplifies the data access layer

### Benefits
- **Simplified Database**: Fewer stored procedures to maintain
- **Better Caching**: All data is cached and filtered in memory
- **Consistent Performance**: No additional database queries for filtering
- **Easier Maintenance**: Single data access pattern

## Security

- All endpoints require authentication
- Users can only create and view their own policies
- Policies are validated server-side before allowing client registration
- Expired policies are automatically deactivated
- Client registration automatically uses the policy owner's user ID 