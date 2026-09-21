# API Error Codes

The public API returns a stable machine-readable `errorCode` together with a customer-facing `errorMessage`.

```json
{
  "errorCode": "PICKUP_INVALID_INPUT",
  "errorMessage": "The provided input was invalid."
}
```

## Contract rules

- **`errorCode` is stable.** Customer integrations should use this value for programmatic handling.
- **`errorMessage` is not stable.** The service owner may change the wording without changing the `errorCode`.
- Error messages must not be parsed by customer applications to determine the type of error.
- The HTTP status code remains part of the API contract and should be checked before the response body.
- The API exposes stable business error codes for known return-state validation failures. Customer applications should use those codes rather than parsing `errorMessage`.

## Error codes

| Error code | HTTP status | Endpoint/context | Meaning |
|---|---:|---|---|
| `AUTH_INVALID_INPUT` | 400 | `POST /oauth/token` | The authentication request input is missing or invalid. |
| `AUTH_INVALID_CREDENTIALS` | 401 | `POST /oauth/token` | The supplied authentication credentials were rejected. |
| `AUTH_REQUIRED` | 401 | Authenticated rental endpoints | A valid tenant access token is required. |
| `PICKUP_INVALID_INPUT` | 400 | `POST /api/rentals/pickup` | The pickup request contains input that cannot be accepted. |
| `RETURN_INVALID_INPUT` | 400 | `POST /api/rentals/{bookingNumber}/return` | The return request is structurally invalid (for example missing/invalid return values). |
| `RETURN_ALREADY_RETURNED` | 400 | `POST /api/rentals/{bookingNumber}/return` | The rental has already been returned. |
| `RETURN_TIME_BEFORE_PICKUP` | 400 | `POST /api/rentals/{bookingNumber}/return` | The return time is before the rental pickup time. |
| `RETURN_ODOMETER_BEFORE_PICKUP` | 400 | `POST /api/rentals/{bookingNumber}/return` | The return odometer is lower than the pickup odometer. |
| `RETURN_RENTAL_NOT_FOUND` | 404 | `POST /api/rentals/{bookingNumber}/return` | No rental accessible to the authenticated tenant could be found for the supplied globally unique booking reference. The response intentionally does not reveal whether another tenant owns the booking. |
| `INTERNAL_ERROR` | 500 | Any endpoint | An unexpected server-side error occurred. Details are logged internally. |

## Ownership and message changes

The stable codes are defined in:

`src/CarRental.Contracts/ErrorCodes.cs`

Customer-facing message text is maintained separately in the API in the `ErrorMessages` dictionary in:

`src/CarRental.Api/Program.cs`

This separation means a message such as:

```text
"The provided input was invalid."
```

can be changed to another customer-facing message without requiring consumers to change code, as long as the associated error code remains unchanged.

## Consumer example

A consumer should branch on the code, not the message:

```text
if errorCode == "PICKUP_INVALID_INPUT"
    handle invalid pickup input

if errorCode == "PICKUP_BOOKING_ALREADY_EXISTS"
    handle duplicate booking
```

The consumer may display `errorMessage` directly to a user or use it for diagnostics, but should not depend on its exact wording.
