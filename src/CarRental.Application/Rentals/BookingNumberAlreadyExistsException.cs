namespace CarRental.Application.Rentals;

public sealed class BookingNumberAlreadyExistsException(string bookingNumber)
    : InvalidOperationException($"Booking number '{bookingNumber}' is already in use.")
{
}
