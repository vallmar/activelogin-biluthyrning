using System.Security.Cryptography;
using System.Text;

namespace CarRental.Infrastructure.FilePersistence;

internal static class FileKey
{
    public static string For(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
