using PdfSharp.Fonts;

namespace CarRental.Infrastructure.FilePersistence;

internal sealed class PortablePdfFontResolver : IFontResolver
{
    private static readonly string[] RegularCandidates =
    [
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/truetype/liberation2/LiberationSans-Regular.ttf",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf")
    ];

    private static readonly string[] BoldCandidates =
    [
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        "/usr/share/fonts/truetype/liberation2/LiberationSans-Bold.ttf",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arialbd.ttf")
    ];

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
        => new(isBold ? "car-rental-bold" : "car-rental-regular", false, isItalic);

    public byte[]? GetFont(string faceName)
    {
        var candidates = faceName == "car-rental-bold"
            ? BoldCandidates
            : RegularCandidates;

        var path = candidates.FirstOrDefault(File.Exists);
        return path is null ? null : File.ReadAllBytes(path);
    }
}
