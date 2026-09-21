namespace CarRental.Domain;

public sealed class ReturnOdometerBeforePickupException : ArgumentException
{
    public ReturnOdometerBeforePickupException()
        : base("Return odometer cannot be lower than pickup odometer.")
    {
    }
}
