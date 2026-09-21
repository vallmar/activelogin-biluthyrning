using CarRental.Domain;
using Xunit;

namespace CarRental.Tests;

public class RentalTests
{
    [Fact]
    public void Cannot_return_a_rental_twice()
    {
        var rental = CreateRental();
        rental.Return(rental.PickupTime.AddDays(1), rental.PickupOdometer + 10);
        var exception = Assert.Throws<RentalAlreadyReturnedException>(() => rental.Return(rental.PickupTime.AddDays(2), rental.PickupOdometer + 20));
        Assert.Equal("Rental has already been returned.", exception.Message);
    }

    [Fact]
    public void Return_odometer_cannot_be_lower_than_pickup_odometer()
    {
        var rental = CreateRental();
        var exception = Assert.Throws<ReturnOdometerBeforePickupException>(() => rental.Return(rental.PickupTime.AddDays(1), rental.PickupOdometer - 1));
        Assert.Equal("Return odometer cannot be lower than pickup odometer.", exception.Message);
    }

    [Fact]
    public void Return_time_cannot_be_before_pickup()
    {
        var rental = CreateRental();
        var exception = Assert.Throws<ReturnTimeBeforePickupException>(() => rental.Return(rental.PickupTime.AddMinutes(-1), rental.PickupOdometer));
        Assert.Equal("Return time cannot be before pickup time.", exception.Message);
    }

    [Fact]
    public void Final_price_can_only_be_set_after_return()
    {
        var rental = CreateRental();
        Assert.Throws<InvalidOperationException>(() => rental.SetFinalPrice(100m));
    }

    [Fact]
    public void Booking_data_is_immutable_after_creation()
    {
        var rental = CreateRental();
        Assert.Equal("B-1", rental.BookingNumber);
        Assert.Equal(CarCategory.SmallCar, rental.Category);
        Assert.Equal(10_000, rental.PickupOdometer);
    }

    private static Rental CreateRental() => new("B-1", "ABC123", "customer-1", CarCategory.SmallCar,
        DateTimeOffset.Parse("2026-01-01T10:00:00+01:00"), 10_000);
}
