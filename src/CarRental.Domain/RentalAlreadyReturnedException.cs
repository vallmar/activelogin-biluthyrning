namespace CarRental.Domain;

public sealed class RentalAlreadyReturnedException : InvalidOperationException
{
    public RentalAlreadyReturnedException()
        : base("Rental has already been returned.")
    {
    }
}
