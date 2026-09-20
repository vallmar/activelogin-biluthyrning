# Customer API documentation

This document describes the HTTP API exposed by the car-rental SaaS.

The API is the integration boundary for customers. Customer applications should treat the HTTP/JSON models documented here as the public contract and should not depend on internal Domain or Application models.

## Authentication and tenant identity

Every customer request to the rental API must use a valid bearer access token.

For this showcase, the API contains a deliberately small local token endpoint:

```http
POST /oauth/token
Content-Type: application/json
```

```json
{
  "clientId": "tenant-a",
  "clientSecret": "secret-a"
}
```

Successful response:

```json
{
  "accessToken": "<signed-jwt>",
  "tokenType": "Bearer",
  "expiresIn": 3600
}
```

The token is then sent on customer API calls:

```http
Authorization: Bearer <accessToken>
```

The API validates the JWT signature, issuer, audience and lifetime. The tenant identity is derived from the trusted `client_id` claim. Customers must never put a tenant identifier in the rental JSON payload.

The local `/oauth/token` endpoint exists only to make this repository self-contained. It is a showcase token issuer, not a production identity provider. A real deployment would use an OAuth 2.0/OIDC identity provider and the API would validate tokens issued by that provider.

## Tenant isolation

Tenant isolation is part of the application behaviour:

- the booking numbers are globally unique across the entire API;
- rental lookups are always scoped to the authenticated tenant;
- a tenant cannot return or modify another tenant's rental;
- an attempt to access another tenant's known booking returns `404 Not Found`, so the API does not disclose that the booking exists for another tenant;
- the application logs a security-relevant warning when it detects such a cross-tenant access attempt.

The external response deliberately does not reveal the owning tenant.

## Response and error handling

The customer API uses a single error response contract for application-generated errors:

```json
{
  "errorCode": "PICKUP_INVALID_INPUT",
  "errorMessage": "The provided input was invalid."
}
```

**`errorCode` is the stable machine-readable value. `errorMessage` is human-readable text and may be changed by the API owner without changing the code.** Customer integrations must never parse or branch on `errorMessage`.

Always check the HTTP status code first, then inspect `errorCode` when the response is an error.

The currently documented codes are maintained in `src/CarRental.Contracts/ErrorCodes.cs` and described in [`docs/ERROR_CODES.md`](ERROR_CODES.md).

### Error-code contract

| Error code | HTTP status | Meaning |
|---|---:|---|
| `AUTH_INVALID_CREDENTIALS` | `401` | The supplied token-endpoint credentials were not accepted. |
| `AUTH_REQUIRED` | `401` | A valid tenant access token is required. |
| `PICKUP_INVALID_INPUT` | `400` | The pickup request input was invalid. |
| `RETURN_INVALID_INPUT` | `400` | The return request input or business state was invalid. |
| `RETURN_RENTAL_NOT_FOUND` | `404` | The rental could not be found for the authenticated tenant. |
| `INTERNAL_ERROR` | `500` | An unexpected server-side error occurred. |

Error messages intentionally do not expose the expected parameter values or underlying implementation details.

### Unexpected server-side failures

Unexpected server-side failures return `500 Internal Server Error` using the same error contract:

```json
{
  "errorCode": "INTERNAL_ERROR",
  "errorMessage": "An unexpected error occurred."
}
```

The server does not expose the underlying exception, stack trace, or other internal implementation details in this response. The underlying error is logged internally for operators.

Framework-level failures such as malformed JSON can be rejected before an application endpoint handler runs. Consumers should therefore always use the HTTP status code as the first level of error handling and should not assume that every framework-generated failure has an application error code.

## POST /oauth/token

Demo token endpoint used by the repository's customer examples.

### Request

```json
{
  "clientId": "tenant-a",
  "clientSecret": "secret-a"
}
```

Two showcase clients are configured:

| Client ID | Demo secret | Tenant |
|---|---|---|
| `tenant-a` | `secret-a` | tenant-a |
| `tenant-b` | `secret-b` | tenant-b |

### Success

HTTP `200 OK`.

```json
{
  "accessToken": "<signed-jwt>",
  "tokenType": "Bearer",
  "expiresIn": 3600
}
```

### Failure

HTTP `401 Unauthorized` when the client credentials are not accepted:

```json
{
  "errorCode": "AUTH_INVALID_CREDENTIALS",
  "errorMessage": "The supplied credentials were invalid."
}
```

## POST /api/rentals/pickup

Registers a vehicle pickup.

### Request

```http
POST /api/rentals/pickup
Authorization: Bearer <accessToken>
Content-Type: application/json
```

```json
{
  "bookingNumber": "TEST-001",
  "registrationNumber": "ABC123",
  "customerIdentifier": "customer-a",
  "category": "SmallCar",
  "pickupTime": "2026-09-15T10:00:00Z",
  "pickupOdometer": 10000
}
```

### Request fields

| Field | Type | Required | Description |
|---|---|---|---|
| `bookingNumber` | string | Yes | Customer's booking identifier. Globally unique across the entire API. |
| `registrationNumber` | string | Yes | Vehicle registration number. |
| `customerIdentifier` | string | Yes | Identifier for the customer making the rental. |
| `category` | string | Yes | `SmallCar`, `Combi`, or `Truck`. |
| `pickupTime` | ISO-8601 timestamp | Yes | Time at which the vehicle was picked up. |
| `pickupOdometer` | integer | Yes | Odometer reading at pickup. Must not be negative. |

### Success

HTTP `201 Created`.

```json
{
  "bookingNumber": "TEST-001",
  "registrationNumber": "ABC123",
  "customerIdentifier": "customer-a",
  "category": "SmallCar",
  "pickupTime": "2026-09-15T10:00:00Z",
  "pickupOdometer": 10000,
  "isReturned": false
}
```

The response also contains a `Location` header pointing to `/api/rentals/{bookingNumber}`.

### Errors

- `400 Bad Request` — request or business rule rejected.
- `401 Unauthorized` — missing or invalid access token.
- `500 Internal Server Error` — unexpected server-side failure.

Example duplicate-booking response:

```json
{
  "errorCode": "PICKUP_BOOKING_ALREADY_EXISTS",
  "errorMessage": "The provided input could not be processed."
}
```

Example invalid-input response:

```json
{
  "errorCode": "PICKUP_INVALID_INPUT",
  "errorMessage": "The provided input was invalid."
}
```

Example unauthorized response:

```json
{
  "errorCode": "AUTH_REQUIRED",
  "errorMessage": "A valid tenant access token is required."
}
```

## POST /api/rentals/{bookingNumber}/return

Registers the return of a rental and calculates the final price.

### Request

```http
POST /api/rentals/TEST-001/return
Authorization: Bearer <accessToken>
Content-Type: application/json
```

```json
{
  "returnTime": "2026-09-15T18:00:00Z",
  "returnOdometer": 10150,
  "baseDailyPrice": 500,
  "baseKmPrice": 2
}
```

### Request fields

| Field | Type | Required | Description |
|---|---|---|---|
| `bookingNumber` | path string | Yes | Globally unique booking number to return. |
| `returnTime` | ISO-8601 timestamp | Yes | Return time. Cannot be before pickup time. |
| `returnOdometer` | integer | Yes | Return odometer. Cannot be below pickup odometer. |
| `baseDailyPrice` | decimal | Yes | Base daily rental price. Must not be negative. |
| `baseKmPrice` | decimal | Yes | Base kilometre price. Must not be negative. |

### Success

HTTP `200 OK`.

```json
{
  "bookingNumber": "TEST-001",
  "finalPrice": 1000
}
```

### Errors

- `400 Bad Request` — rental exists but the return violates a business rule or contains invalid request values.
- `401 Unauthorized` — missing or invalid access token.
- `404 Not Found` — booking does not exist for the authenticated tenant, including when another tenant owns it.
- `500 Internal Server Error` — unexpected server-side failure.

Example invalid return response:

```json
{
  "errorCode": "RETURN_INVALID_INPUT",
  "errorMessage": "The provided input was invalid."
}
```

Example not-found response:

```json
{
  "errorCode": "RETURN_RENTAL_NOT_FOUND",
  "errorMessage": "The provided input could not be processed."
}
```

The `404` response is intentionally identical whether the booking does not exist or belongs to another tenant.

Example unauthorized response:

```json
{
  "errorCode": "AUTH_REQUIRED",
  "errorMessage": "A valid tenant access token is required."
}
```

## Customer integration flow

```text
Customer application
       |
       | POST /oauth/token
       | client credentials
       v
   access token
       |
       | Authorization: Bearer <JWT>
       v
    Rental API
       |
       +--> validate JWT
       |
       +--> client_id -> tenant context
       |
       +--> tenant-scoped rental operation
       |
       +--> 2xx / 4xx / 5xx response
               |
               +--> HTTP status
               +--> errorCode (stable)
               +--> errorMessage (editable text)
```

Customer applications should branch on `errorCode`, for example:

```csharp
if (!response.IsSuccessStatusCode)
{
    var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();

    switch (error?.ErrorCode)
    {
        case ErrorCodes.PickupInvalidInput:
            // Handle invalid pickup input.
            break;
        case ErrorCodes.PickupBookingAlreadyExists:
            // Handle duplicate booking.
            break;
        default:
            // Handle other documented or unknown errors.
            break;
    }
}
```

Unknown future error codes must be handled safely as an unknown error. Customers must not depend on the exact set of codes never changing.

## Security and logging

The API never logs bearer access tokens or client secrets.

When the API detects that an authenticated tenant requested a booking owned by another tenant, it emits a structured Warning for operators. The caller still receives `404 Not Found` so the existence or ownership of the booking is not disclosed.

The security log contains internal diagnostic context such as the requesting tenant, booking number, and owning tenant. This context is for service operators and is not part of the customer response.

## Contract summary

| Endpoint | Success | Error codes |
|---|---:|---|
| `POST /oauth/token` | `200 OK` | `AUTH_INVALID_CREDENTIALS` |
| `POST /api/rentals/pickup` | `201 Created` | `PICKUP_INVALID_INPUT`, | `POST /api/rentals/{bookingNumber}/return` | `200 OK` | `RETURN_INVALID_INPUT`, `RETURN_RENTAL_NOT_FOUND`, `AUTH_REQUIRED`, `INTERNAL_ERROR` |

The stable error-code definitions are in `src/CarRental.Contracts/ErrorCodes.cs`. The customer-facing message text is owned by the API implementation and can change independently of the codes.
