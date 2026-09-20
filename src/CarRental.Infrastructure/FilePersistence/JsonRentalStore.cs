using System.Text.Json;
using CarRental.Application.Ports;
using CarRental.Domain;

namespace CarRental.Infrastructure.FilePersistence;

public sealed class JsonRentalStore(string directory) : IRentalStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<Rental?> GetAsync(string bookingNumber, CancellationToken cancellationToken = default)
    {
        var path = GetPath(bookingNumber);
        if (!File.Exists(path))
            return null;

        await using var stream = File.OpenRead(path);
        var document = await JsonSerializer.DeserializeAsync<RentalDocument>(
            stream, Options, cancellationToken);

        return document?.ToRental();
    }

    public async Task CreateAsync(Rental rental, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        var path = GetPath(rental.BookingNumber);

        if (File.Exists(path))
            throw new InvalidOperationException("Booking number is already in use.");

        await WriteAsync(path, RentalDocument.FromRental(rental), cancellationToken);
    }

    public Task UpdateAsync(Rental rental, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        return WriteAsync(GetPath(rental.BookingNumber), RentalDocument.FromRental(rental), cancellationToken);
    }

    private async Task WriteAsync(string path, RentalDocument document, CancellationToken cancellationToken)
    {
        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";

        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, document, Options, cancellationToken);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }

    private string GetPath(string bookingNumber)
        => Path.Combine(directory, $"{FileKey.For(bookingNumber)}.json");
}
