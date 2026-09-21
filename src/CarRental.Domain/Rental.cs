namespace CarRental.Domain;

public sealed class Rental
{
    public string TenantId { get; }
    public string BookingNumber { get; }
    public string RegistrationNumber { get; }
    public string CustomerIdentifier { get; }
    public CarCategory Category { get; }
    public DateTimeOffset PickupTime { get; }
    public int PickupOdometer { get; }
    public DateTimeOffset? ReturnTime { get; private set; }
    public int? ReturnOdometer { get; private set; }
    public decimal? FinalPrice { get; private set; }

    public bool IsReturned => ReturnTime.HasValue;

    public Rental(string tenantId, string bookingNumber, string registrationNumber, string customerIdentifier,
        CarCategory category, DateTimeOffset pickupTime, int pickupOdometer)
    {
        if (string.IsNullOrWhiteSpace(tenantId)) throw new ArgumentException("Tenant id is required.");
        if (string.IsNullOrWhiteSpace(bookingNumber)) throw new ArgumentException("Booking number is required.");
        if (string.IsNullOrWhiteSpace(registrationNumber)) throw new ArgumentException("Registration number is required.");
        if (string.IsNullOrWhiteSpace(customerIdentifier)) throw new ArgumentException("Customer identifier is required.");
        if (pickupOdometer < 0) throw new ArgumentOutOfRangeException(nameof(pickupOdometer), "Pickup odometer cannot be negative.");

        TenantId = tenantId;
        BookingNumber = bookingNumber;
        RegistrationNumber = registrationNumber;
        CustomerIdentifier = customerIdentifier;
        Category = category;
        PickupTime = pickupTime;
        PickupOdometer = pickupOdometer;
    }

    // Keeps direct domain tests simple; application-created rentals always receive the authenticated tenant explicitly.
    public Rental(string bookingNumber, string registrationNumber, string customerIdentifier,
        CarCategory category, DateTimeOffset pickupTime, int pickupOdometer)
        : this("test-tenant", bookingNumber, registrationNumber, customerIdentifier, category, pickupTime, pickupOdometer)
    {
    }

    public void Return(DateTimeOffset returnTime, int returnOdometer)
    {
        if (IsReturned) throw new RentalAlreadyReturnedException();
        if (returnTime < PickupTime) throw new ReturnTimeBeforePickupException();
        if (returnOdometer < PickupOdometer)
            throw new ReturnOdometerBeforePickupException();

        ReturnTime = returnTime;
        ReturnOdometer = returnOdometer;
    }

    public void SetFinalPrice(decimal finalPrice)
    {
        if (!IsReturned) throw new InvalidOperationException("A rental must be returned before a final price can be set.");
        if (finalPrice < 0)
            throw new ArgumentOutOfRangeException(nameof(finalPrice), "Final price cannot be negative.");
        FinalPrice = finalPrice;
    }
}
