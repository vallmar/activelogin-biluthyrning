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

        await service.RegisterPickupAsync(
            "tenant-a", "B-1", "ABC123", "customer-1", CarCategory.Combi,
            DateTimeOffset.Parse("2026-01-01T10:00:00+01:00"), 10_000,
            TestContext.Current.CancellationToken);

        var price = await service.RegisterReturnAsync(
            "tenant-a", "B-1",
            DateTimeOffset.Parse("2026-01-03T10:00:00+01:00"), 10_100,
            new Pricing(500m, 2m), TestContext.Current.CancellationToken);

        Assert.Equal(1500m, price);
        var rental = await store.GetAsync("B-1", TestContext.Current.CancellationToken);
        Assert.NotNull(rental);
        Assert.Equal(1500m, rental!.FinalPrice);
    }

    [Fact]
    public async Task Booking_number_must_be_globally_unique_across_tenants()
    {
        var tenantAStore = new InMemoryRentalStore();
        var tenantBStore = new InMemoryRentalStore();
        var resolver = new DictionaryStoreResolver(("tenant-a", tenantAStore), ("tenant-b", tenantBStore));
        var registry = new InMemoryBookingNumberRegistry();
        var service = new RentalService(resolver, registry, new PriceCalculator());

        await service.RegisterPickupAsync(
            "tenant-a", "B-1", "ABC123", "customer-a", CarCategory.SmallCar,
            DateTimeOffset.UtcNow, 10_000, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<BookingNumberAlreadyExistsException>(() =>
            service.RegisterPickupAsync(
                "tenant-b", "B-1", "XYZ789", "customer-b", CarCategory.Truck,
                DateTimeOffset.UtcNow, 20_000, TestContext.Current.CancellationToken));

        Assert.NotNull(await tenantAStore.GetAsync("B-1"));
        Assert.Null(await tenantBStore.GetAsync("B-1"));
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

    private sealed class DictionaryStoreResolver(params (string TenantId, IRentalStore Store)[] entries) : IRentalStoreResolver
    {
        private readonly Dictionary<string, IRentalStore> stores =
            entries.ToDictionary(x => x.TenantId, x => x.Store, StringComparer.Ordinal);

        public IRentalStore Resolve(string tenantId) => stores[tenantId];
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
