using CarRental.Domain;

namespace CarRental.Application.Ports;

/// <summary>
/// Persistence contract for one customer/tenant.
/// The implementation can be JSON, PDF, PostgreSQL, SQL Server, or a customer-specific adapter.
/// </summary>
public interface IRentalStore
{
    Task<Rental?> GetAsync(string bookingNumber, CancellationToken cancellationToken = default);
    Task CreateAsync(Rental rental, CancellationToken cancellationToken = default);
    Task UpdateAsync(Rental rental, CancellationToken cancellationToken = default);
}
