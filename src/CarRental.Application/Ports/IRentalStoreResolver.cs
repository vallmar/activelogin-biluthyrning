namespace CarRental.Application.Ports;

public interface IRentalStoreResolver
{
    IRentalStore Resolve(string tenantId);
}
