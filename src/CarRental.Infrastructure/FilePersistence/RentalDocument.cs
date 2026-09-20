using CarRental.Domain;

namespace CarRental.Infrastructure.FilePersistence;

internal sealed record RentalDocument(
    string TenantId,
    string BookingNumber,
    string RegistrationNumber,
    string CustomerIdentifier,
    CarCategory Category,
    DateTimeOffset PickupTime,
    int PickupOdometer,
    DateTimeOffset? ReturnTime,
    int? ReturnOdometer,
    decimal? FinalPrice)
{
    public static RentalDocument FromRental(Rental rental) => new(
        rental.TenantId,
        rental.BookingNumber,
        rental.RegistrationNumber,
        rental.CustomerIdentifier,
        rental.Category,
        rental.PickupTime,
        rental.PickupOdometer,
        rental.ReturnTime,
        rental.ReturnOdometer,
        rental.FinalPrice);

    public Rental ToRental()
    {
        var rental = new Rental(
            TenantId,
            BookingNumber,
            RegistrationNumber,
            CustomerIdentifier,
            Category,
            PickupTime,
            PickupOdometer);

        if (ReturnTime is not null && ReturnOdometer is not null)
            rental.Return(ReturnTime.Value, ReturnOdometer.Value);

        if (FinalPrice is not null)
            rental.SetFinalPrice(FinalPrice.Value);

        return rental;
    }
}
