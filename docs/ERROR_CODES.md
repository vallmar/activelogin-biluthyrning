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
- The API does not expose the underlying exception or the specific invalid parameter in these customer-facing messages.

## Error codes

| Error code | HTTP status | Endpoint/context | Meaning |
|---|---:|---|---|
| `AUTH_INVALID_CREDENTIALS` | 401 | `POST /oauth/token` | The supplied authentication credentials were rejected. |
| `AUTH_REQUIRED` | 401 | Authenticated rental endpoints | A valid tenant access token is required. |
| `PICKUP_INVALID_INPUT` | 400 | `POST /api/rentals/pickup` | The pickup request contains input that cannot be accepted. |
| `PICKUP_BOOKING_ALREADY_EXISTS` | 400 | `POST /api/rentals/pickup` | The pickup cannot be registered because the booking is already in use for the authenticated tenant. |
| `RETURN_INVALID_INPUT` | 400 | `POST /api/rentals/{bookingNumber}/return` | The return request contains input or a rental state that cannot be accepted. |
| `RETURN_RENTAL_NOT_FOUND` | 404 | `POST /api/rentals/{bookingNumber}/return` | No rental accessible to the authenticated tenant could be found for the supplied booking reference. The response intentionally does not reveal whether another tenant owns the booking. |
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
