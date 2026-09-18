using System.Text;
using System.Text.Json;
using CarRental.Application.Ports;
using CarRental.Domain;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace CarRental.Infrastructure.FilePersistence;

/// <summary>
/// Deliberately simple document persistence adapter for the showcase.
/// Each pickup and return is persisted as its own PDF snapshot. The PDF document
/// metadata contains the machine-readable snapshot so the PDF itself remains the source of truth.
/// </summary>
public sealed class PdfRentalStore(string directory) : IRentalStore
{
    private static int fontResolverConfigured;

    public async Task<Rental?> GetAsync(string bookingNumber, CancellationToken cancellationToken = default)
    {
        var returnPath = GetPath(bookingNumber, "return");
        var pickupPath = GetPath(bookingNumber, "pickup");

        if (File.Exists(returnPath))
            return await ReadSnapshotAsync(returnPath, cancellationToken);

        if (File.Exists(pickupPath))
            return await ReadSnapshotAsync(pickupPath, cancellationToken);

        return null;
    }

    public async Task CreateAsync(Rental rental, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        var path = GetPath(rental.BookingNumber, "pickup");

        if (File.Exists(path) || File.Exists(GetPath(rental.BookingNumber, "return")))
            throw new InvalidOperationException("Booking number is already in use.");

        await WriteSnapshotAsync(path, rental, "PICKUP", cancellationToken);
    }

    public async Task UpdateAsync(Rental rental, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        await WriteSnapshotAsync(
            GetPath(rental.BookingNumber, "return"),
            rental,
            "RETURN",
            cancellationToken);
    }

    private async Task WriteSnapshotAsync(
        string path,
        Rental rental,
        string eventType,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ConfigureFonts();

        var document = new PdfDocument();
        document.Info.Title = $"Car rental {rental.BookingNumber} - {eventType}";
        document.Info.Author = "Car Rental SaaS";
        document.Info.Subject = EncodeSnapshot(RentalDocument.FromRental(rental));

        var page = document.AddPage();
        page.Size = PageSize.A4;

        var graphics = XGraphics.FromPdfPage(page);
        var titleFont = new XFont("Arial", 20, XFontStyleEx.Bold);
        var bodyFont = new XFont("Arial", 11, XFontStyleEx.Regular);

        var y = 60;
        graphics.DrawString(
            $"Car Rental - {eventType}",
            titleFont,
            XBrushes.Black,
            50, y);

        y += 40;

        foreach (var line in GetDisplayLines(rental))
        {
            graphics.DrawString(
                line,
                bodyFont,
                XBrushes.Black,
                50, y);
            y += 22;
        }

        await using var stream = new MemoryStream();
        document.Save(stream, false);

        stream.Position = 0;
        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";

        await using (var output = File.Create(temporaryPath))
        {
            await stream.CopyToAsync(output, cancellationToken);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }

    private static async Task<Rental?> ReadSnapshotAsync(
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var stream = File.OpenRead(path);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);

        using var document = PdfReader.Open(
            new MemoryStream(memory.ToArray()),
            PdfDocumentOpenMode.Import);

        var encoded = document.Info.Subject;
        if (string.IsNullOrWhiteSpace(encoded))
            throw new InvalidDataException($"PDF rental document '{path}' has no rental snapshot.");

        return DecodeSnapshot(encoded).ToRental();
    }

    private string GetPath(string bookingNumber, string eventType)
        => Path.Combine(directory, $"{FileKey.For(bookingNumber)}.{eventType}.pdf");

    private static string EncodeSnapshot(RentalDocument document)
    {
        var json = JsonSerializer.Serialize(document);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    private static RentalDocument DecodeSnapshot(string encoded)
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        return JsonSerializer.Deserialize<RentalDocument>(json)
            ?? throw new InvalidDataException("PDF rental snapshot could not be deserialized.");
    }

    private static IEnumerable<string> GetDisplayLines(Rental rental)
    {
        yield return $"Booking number: {rental.BookingNumber}";
        yield return $"Tenant: {rental.TenantId}";
        yield return $"Registration: {rental.RegistrationNumber}";
        yield return $"Customer: {rental.CustomerIdentifier}";
        yield return $"Category: {rental.Category}";
        yield return $"Pickup: {rental.PickupTime:O}";
        yield return $"Pickup odometer: {rental.PickupOdometer}";

        if (rental.ReturnTime is not null)
            yield return $"Return: {rental.ReturnTime:O}";

        if (rental.ReturnOdometer is not null)
            yield return $"Return odometer: {rental.ReturnOdometer}";

        if (rental.FinalPrice is not null)
            yield return $"Final price: {rental.FinalPrice:0.00}";
    }

    private static void ConfigureFonts()
    {
        if (Interlocked.Exchange(ref fontResolverConfigured, 1) == 0)
            GlobalFontSettings.FontResolver = new PortablePdfFontResolver();
    }
}
