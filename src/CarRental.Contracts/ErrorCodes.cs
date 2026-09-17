namespace CarRental.Contracts;

/// <summary>
/// Stable machine-readable error codes returned by the public API.
/// Error messages may change without changing these codes.
/// </summary>
public static class ErrorCodes
{
    public const string AuthenticationInvalidCredentials = "AUTH_INVALID_CREDENTIALS";
    public const string AuthenticationRequired = "AUTH_REQUIRED";
    public const string PickupInvalidInput = "PICKUP_INVALID_INPUT";
    public const string PickupBookingAlreadyExists = "PICKUP_BOOKING_ALREADY_EXISTS";
    public const string ReturnInvalidInput = "RETURN_INVALID_INPUT";
    public const string ReturnRentalNotFound = "RETURN_RENTAL_NOT_FOUND";
    public const string InternalError = "INTERNAL_ERROR";
}
