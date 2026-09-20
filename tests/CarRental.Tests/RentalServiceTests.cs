using CarRental.Application.Ports;
using CarRental.Application.Pricing;
using CarRental.Application.Rentals;
using CarRental.Domain;
using Xunit;

namespace CarRental.Tests;

public sealed class RentalServiceTests
{
    // These tests exercise the application workflow directly. They intentionally use small test doubles for the persistence ports; they do not test private implementation details.
    [Fact]
    public async Task Register_return_calculates_and_persists_final_price()
    {
        var store = new InMemoryRentalStore();
        var service = new RentalService(new TestStoreResolver(store), new InMemoryBookingNumberRegistry(), new PriceCalculator());

        var rental = await service.RegisterPickupAsync(
            "tenant-a", "ABC123", "customer-1", CarCategory.Combi,
            DateTimeOffset.Parse("2026-01-01T10:00:00+01:00"), 10_000,
            TestContext.Current.CancellationToken);

        var price = await service.RegisterReturnAsync(
            "tenant-a", rental.BookingNumber,
            DateTimeOffset.Parse("2026-01-03T10:00:00+01:00"), 10_100,
            new Pricing(500m, 2m), TestContext.Current.CancellationToken);

        Assert.Equal(1500m, price);
        var persistedRental = await store.GetAsync(rental.BookingNumber, TestContext.Current.CancellationToken);
        Assert.NotNull(persistedRental);
        Assert.Equal(1500m, persistedRental!.FinalPrice);
    }

    // The resolver/store doubles below are deliberate test seams for the application ports; they avoid testing concrete infrastructure from application tests.

    [Fact]
    public async Task Unknown_booking_number_fails_on_return()
    {
        var service = new RentalService(new TestStoreResolver(new InMemoryRentalStore()), new InMemoryBookingNumberRegistry(), new PriceCalculator());

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.RegisterReturnAsync(
                "tenant-a", "missing",
                DateTimeOffset.Parse("2026-01-03T10:00:00+01:00"), 10_100,
                new Pricing(500m, 2m), TestContext.Current.CancellationToken));
    }

    private sealed class TestStoreResolver(IRentalStore store) : IRentalStoreResolver
    {
        public IRentalStore Resolve(string tenantId) => store;
    }

    private sealed class InMemoryBookingNumberRegistry : IBookingNumberRegistry
    {
        private readonly HashSet<string> bookingNumbers = new(StringComparer.Ordinal);

        public Task<bool> TryReserveAsync(string bookingNumber, string tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult(bookingNumbers.Add(bookingNumber));

        public Task ReleaseAsync(string bookingNumber, string tenantId, CancellationToken cancellationToken = default)
        {
            bookingNumbers.Remove(bookingNumber);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryRentalStore : IRentalStore
    {
        private readonly Dictionary<string, Rental> rentals = new(StringComparer.Ordinal);

        public Task<Rental?> GetAsync(string bookingNumber, CancellationToken cancellationToken = default)
            => Task.FromResult(rentals.GetValueOrDefault(bookingNumber));

        public Task CreateAsync(Rental rental, CancellationToken cancellationToken = default)
        {
            if (!rentals.TryAdd(rental.BookingNumber, rental))
                throw new InvalidOperationException("Booking number is already in use.");
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Rental rental, CancellationToken cancellationToken = default)
        {
            rentals[rental.BookingNumber] = rental;
            return Task.CompletedTask;
        }
    }
}
