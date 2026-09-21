# Customer API documentation

This document describes the public HTTP API exposed by the car-rental SaaS.

Customers integrate through HTTP and the contracts below. Customer applications do not reference the internal Domain or Application projects.

## Authentication

### POST /oauth/token

The repository contains a small demo token endpoint so the examples are self-contained. Production deployments use the agreed OAuth 2.0/OIDC identity provider.

Request:

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

Success:

```json
{
  "accessToken": "<signed-jwt>",
  "tokenType": "Bearer",
  "expiresIn": 3600
}
```

Send the token on protected calls:

```http
Authorization: Bearer <accessToken>
```

The validated `client_id` claim determines the tenant. Tenant identity is never supplied as a business field.

## POST /api/rentals/pickup

Registers a vehicle pickup.

### Request

The application generates the `bookingNumber`. **Customers must not send a booking number in the pickup request.**

```http
POST /api/rentals/pickup
Authorization: Bearer <accessToken>
Content-Type: application/json
```

```json
{
  "registrationNumber": "ABC123",
  "customerIdentifier": "customer-a",
  "category": "SmallCar",
  "pickupTime": "2026-09-15T10:00:00Z",
  "pickupOdometer": 10000
}
```

Fields:

| Field | Type | Required | Rule |
|---|---|---:|---|
| `registrationNumber` | string | Yes | Vehicle registration number. |
| `customerIdentifier` | string | Yes | Customer identifier. |
| `category` | string | Yes | `SmallCar`, `Combi`, or `Truck`. |
| `pickupTime` | ISO-8601 timestamp | Yes | Pickup timestamp. |
| `pickupOdometer` | integer | Yes | Must be >= 0. |

Success: **201 Created**

```json
{
  "bookingNumber": "R-generated-by-the-application",
  "registrationNumber": "ABC123",
  "customerIdentifier": "customer-a",
  "category": "SmallCar",
  "pickupTime": "2026-09-15T10:00:00Z",
  "pickupOdometer": 10000,
  "isReturned": false
}
```

The `Location` header points to `/api/rentals/{bookingNumber}`.

The returned booking number is globally unique across the API and must be retained by the customer application for later operations such as return.

## POST /api/rentals/{bookingNumber}/return

Registers the return and calculates the final price.

### Request

```http
POST /api/rentals/R-generated-by-the-application/return
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

Fields:

| Field | Type | Required | Rule |
|---|---|---:|---|
| `bookingNumber` | path string | Yes | The globally unique booking number returned by pickup. |
| `returnTime` | ISO-8601 timestamp | Yes | Cannot be before pickup time. |
| `returnOdometer` | integer | Yes | Cannot be below pickup odometer. |
| `baseDailyPrice` | decimal | Yes | Must be >= 0. |
| `baseKmPrice` | decimal | Yes | Must be >= 0. |

Success: **200 OK**

```json
{
  "bookingNumber": "R-generated-by-the-application",
  "finalPrice": 850
}
```

## Error handling

Always check the HTTP status first and then inspect the stable `errorCode`.

| Error code | HTTP status | Meaning |
|---|---:|---|
| `AUTH_INVALID_INPUT` | 400 | Authentication request input is missing or invalid. |
| `AUTH_INVALID_CREDENTIALS` | 401 | Authentication credentials were rejected. |
| `AUTH_REQUIRED` | 401 | A valid bearer token is required. |
| `PICKUP_INVALID_INPUT` | 400 | Pickup input cannot be accepted. |
| `RETURN_INVALID_INPUT` | 400 | Return request input is structurally invalid. |
| `RETURN_ALREADY_RETURNED` | 400 | The rental has already been returned. |
| `RETURN_TIME_BEFORE_PICKUP` | 400 | The return time is before the pickup time. |
| `RETURN_ODOMETER_BEFORE_PICKUP` | 400 | The return odometer is below the pickup odometer. |
| `RETURN_RENTAL_NOT_FOUND` | 404 | No rental accessible to the authenticated tenant exists for the booking. |
| `INTERNAL_ERROR` | 500 | Unexpected server-side failure. |

Example:

```json
{
  "errorCode": "RETURN_RENTAL_NOT_FOUND",
  "errorMessage": "The provided input could not be processed."
}
```

Do not parse `errorMessage`; its wording can change.

Unknown future error codes must be handled as unknown errors.

Framework-level failures such as malformed JSON may be rejected before an application handler creates an `errorCode`. Use the HTTP status as the first level of error handling.

## Tenant isolation

Booking numbers are globally unique across the API. Rental operations are resolved through the authenticated tenant.

If a tenant tries to return a booking owned by another tenant, the API returns `404 RETURN_RENTAL_NOT_FOUND` and does not disclose that another tenant owns the booking.

## Customer flow

```text
1. Obtain access token
2. POST /api/rentals/pickup
3. Read generated bookingNumber from the 201 response
4. Store that bookingNumber in the customer application
5. POST /api/rentals/{bookingNumber}/return when the vehicle is returned
6. Handle HTTP status + stable errorCode
```

The canonical onboarding document is `docs/CUSTOMER_GUIDE.md`.
