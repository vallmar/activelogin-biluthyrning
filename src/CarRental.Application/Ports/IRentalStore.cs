using CarRental.Domain;

namespace CarRental.Application.Ports;

/// <summary>
/// Persistence contract for one customer/tenant.
/// The shipped reference implementation uses PDF documents.
/// Other technologies are added as customer-specific adapters behind this contract.
/// </summary>
public interface IRentalStore
{
    Task<Rental?> GetAsync(string bookingNumber, CancellationToken cancellationToken = default);
    Task CreateAsync(Rental rental, CancellationToken cancellationToken = default);
    Task UpdateAsync(Rental rental, CancellationToken cancellationToken = default);
}
