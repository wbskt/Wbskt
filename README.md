# WBSKT


## Registration Policy
- Indefinite number of clients or time
    - Secure, means server has a key which can be used to validate id/hash of the clients. Each hash of id will be generated and preconfigured into the client.
- Limited number of clients
    - 1 client for one time use


## Client registration

### Types
- Manual
    - This means we’ll pass in a key to the client library and invoke registration.
    - This key may be the policy key. This client will be tied up with the policy.
- Auto
    - Secure, the client will invoke the registration with the pre-configured id/hashes.
    - This will probably be used by start-ups.

### Workflow
- Client will invoke the registration endpoint to the core server.
- Core server will validate the request and policy and will provide an auth-token tied to the user and the policy.
    - Also, this token will contain the address of the socket server to which it needs to connect.
- Client will then use this token and the socket server address to connect to the socket server.
- Client will always have a persistent socket connection with the socket server.


## Database

### Users
- Id : INT
- Name : NVARCHAR(100)
- Email : NVARCHAR(100)
- PasswordHash: VARCHAR (512)

### RegistrationPolicies
- RefId : GUID
- Id : INT
- Name : NVARCHAR(100)
- UserId : INT
- MaxClients: INT NULLABLE
- Expiry: DATETIME NULLABLE
- Pin: INT

### Clients
- UserId : INT
- RegistrationPolicyId : INT
- Name : NVARCHAR(100) NULLABLE
- Id : INT
- RefId : GUID
- Active : BOOL
