using CarRental.Application.Ports;
using CarRental.Application.Pricing;
using CarRental.Domain;

namespace CarRental.Application.Rentals;

public sealed class RentalService(
    IRentalStoreResolver storeResolver,
    IBookingNumberRegistry bookingNumberRegistry,
    PriceCalculator priceCalculator)
{
    public async Task<Rental> RegisterPickupAsync(
        string tenantId,
        string bookingNumber,
        string registrationNumber,
        string customerIdentifier,
        CarCategory category,
        DateTimeOffset pickupTime,
        int pickupOdometer,
        CancellationToken cancellationToken = default)
    {
        var store = storeResolver.Resolve(tenantId);

        if (!await bookingNumberRegistry.TryReserveAsync(bookingNumber, tenantId, cancellationToken))
            throw new InvalidOperationException("Booking number is already in use.");

        var rental = new Rental(
            tenantId,
            bookingNumber,
            registrationNumber,
            customerIdentifier,
            category,
            pickupTime,
            pickupOdometer);

        try
        {
            await store.CreateAsync(rental, cancellationToken);
            return rental;
        }
        catch
        {
            await bookingNumberRegistry.ReleaseAsync(bookingNumber, tenantId, cancellationToken);
            throw;
        }
    }

    public async Task<decimal> RegisterReturnAsync(
        string tenantId,
        string bookingNumber,
        DateTimeOffset returnTime,
        int returnOdometer,
        CarRental.Domain.Pricing pricing,
        CancellationToken cancellationToken = default)
    {
        var store = storeResolver.Resolve(tenantId);
        var rental = await store.GetAsync(bookingNumber, cancellationToken);

        if (rental is null)
            throw new KeyNotFoundException($"Rental '{bookingNumber}' was not found.");

        rental.Return(returnTime, returnOdometer);
        var price = priceCalculator.Calculate(rental, pricing);
        rental.SetFinalPrice(price);

        await store.UpdateAsync(rental, cancellationToken);
        return price;
    }
}
