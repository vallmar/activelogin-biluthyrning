namespace CarRental.Application.Ports;

public interface IBookingNumberRegistry
{
    Task<bool> TryReserveAsync(
        string bookingNumber,
        string tenantId,
        CancellationToken cancellationToken = default);

    Task ReleaseAsync(
        string bookingNumber,
        string tenantId,
        CancellationToken cancellationToken = default);
}
