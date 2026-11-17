using System.Security.Cryptography;
using System.Text;

namespace PristonTale.Application.Services;

public static class BrowserLoginTicket
{
    private const string Prefix = "\u001favalon:";
    private const int TokenBytes = 32;
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(45);

    public static string Generate() => Prefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(TokenBytes));

    public static bool IsTicket(string secret) =>
        secret.Length == Prefix.Length + TokenBytes * 2 && secret.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
