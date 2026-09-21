namespace CarRental.Domain;

public sealed class ReturnTimeBeforePickupException : ArgumentException
{
    public ReturnTimeBeforePickupException()
        : base("Return time cannot be before pickup time.")
    {
    }
}
