using CarRental.Domain;
using CarRental.Infrastructure.FilePersistence;
using Xunit;

namespace CarRental.Tests;

public sealed class PersistenceStoreTests
{
    // These are adapter contract tests: they verify behavior across the persistence boundary, not private helper methods.
    [Fact]
    public async Task Json_store_survives_a_new_store_instance()
    {
        var directory = CreateDirectory();

        try
        {
            var rental = CreateRental();
            var store = new JsonRentalStore(directory);

            await store.CreateAsync(rental, TestContext.Current.CancellationToken);

            var reopenedStore = new JsonRentalStore(directory);
            var loaded = await reopenedStore.GetAsync(rental.BookingNumber, TestContext.Current.CancellationToken);

            Assert.NotNull(loaded);
            Assert.Equal(rental.BookingNumber, loaded!.BookingNumber);
            Assert.Equal(rental.TenantId, loaded.TenantId);
            Assert.Equal(rental.PickupOdometer, loaded.PickupOdometer);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task Pdf_store_writes_a_pickup_pdf_and_can_reopen_it()
    {
        var directory = CreateDirectory();

        try
        {
            var rental = CreateRental();
            var store = new PdfRentalStore(directory);

            await store.CreateAsync(rental, TestContext.Current.CancellationToken);

            Assert.Single(Directory.GetFiles(directory, "*.pickup.pdf"));

            var reopenedStore = new PdfRentalStore(directory);
            var loaded = await reopenedStore.GetAsync(rental.BookingNumber, TestContext.Current.CancellationToken);

            Assert.NotNull(loaded);
            Assert.Equal(rental.BookingNumber, loaded!.BookingNumber);
            Assert.Equal(rental.RegistrationNumber, loaded.RegistrationNumber);
            Assert.Equal(rental.PickupOdometer, loaded.PickupOdometer);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task Pdf_store_keeps_the_pickup_pdf_and_adds_a_return_pdf()
    {
        var directory = CreateDirectory();

        try
        {
            var rental = CreateRental();
            var store = new PdfRentalStore(directory);

            await store.CreateAsync(rental, TestContext.Current.CancellationToken);

            rental.Return(rental.PickupTime.AddDays(1), rental.PickupOdometer + 100);
            rental.SetFinalPrice(850m);
            await store.UpdateAsync(rental, TestContext.Current.CancellationToken);

            Assert.Single(Directory.GetFiles(directory, "*.pickup.pdf"));
            Assert.Single(Directory.GetFiles(directory, "*.return.pdf"));

            var reopenedStore = new PdfRentalStore(directory);
            var loaded = await reopenedStore.GetAsync(rental.BookingNumber, TestContext.Current.CancellationToken);

            Assert.NotNull(loaded);
            Assert.True(loaded!.IsReturned);
            Assert.Equal(850m, loaded.FinalPrice);
            Assert.Equal(10100, loaded.ReturnOdometer);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static Rental CreateRental() =>
        new(
            "tenant-a",
            "PDF-TEST-001",
            "ABC123",
            "customer-1",
            CarCategory.Combi,
            DateTimeOffset.Parse("2026-01-01T10:00:00+01:00"),
            10_000);

    private static string CreateDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "CarRentalTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
