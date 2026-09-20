using System.Text.Json;
using CarRental.Application.Ports;

namespace CarRental.Infrastructure.Persistence;

/// <summary>
/// Reference implementation of a platform-wide booking-number registry.
/// Production deployments should back this contract with a central durable store
/// and enforce a unique constraint on BookingNumber.
/// </summary>
public sealed class FileBookingNumberRegistry(string path) : IBookingNumberRegistry
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<bool> TryReserveAsync(
        string bookingNumber,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var reservations = await ReadAsync(cancellationToken);
            if (reservations.ContainsKey(bookingNumber))
                return false;

            reservations[bookingNumber] = tenantId;
            await WriteAsync(reservations, cancellationToken);
            return true;
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task ReleaseAsync(
        string bookingNumber,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var reservations = await ReadAsync(cancellationToken);
            if (reservations.TryGetValue(bookingNumber, out var owner) &&
                string.Equals(owner, tenantId, StringComparison.Ordinal))
            {
                reservations.Remove(bookingNumber);
                await WriteAsync(reservations, cancellationToken);
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<Dictionary<string, string>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(
                   stream, cancellationToken: cancellationToken)
               ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private async Task WriteAsync(
        Dictionary<string, string> reservations,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";

        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, reservations, cancellationToken: cancellationToken);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }
}
