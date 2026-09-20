using CarRental.Application.Ports;
using Microsoft.Extensions.Configuration;

namespace CarRental.Infrastructure.Persistence;

public sealed class ConfiguredRentalStoreResolver(
    IConfiguration configuration,
    string contentRootPath) : IRentalStoreResolver
{
    private readonly Dictionary<string, IRentalStore> stores =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly object sync = new();

    public IRentalStore Resolve(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));

        lock (sync)
        {
            if (stores.TryGetValue(tenantId, out var existing))
                return existing;

            var provider =
                configuration[$"Persistence:Tenants:{tenantId}:Provider"]
                ?? throw new InvalidOperationException(
                    $"No persistence provider is configured for tenant '{tenantId}'.");

            var directory = Path.Combine(
                contentRootPath,
                configuration["Persistence:RootDirectory"] ?? "data",
                tenantId);

            IRentalStore store = provider.ToLowerInvariant() switch
            {
                "json" => new FilePersistence.JsonRentalStore(directory),
                "pdf" => new FilePersistence.PdfRentalStore(directory),
                _ => throw new InvalidOperationException(
                    $"Unknown persistence provider '{provider}' for tenant '{tenantId}'.")
            };

            stores[tenantId] = store;
            return store;
        }
    }
}
