using System.Security.Cryptography;
using System.Text;
namespace Hive.Infrastructure;

public static class ProtectedText
{
    public static string Protect(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        try { return Convert.ToBase64String(new DpapiSecretProtector().Protect(bytes)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public static string Read(string? payload)
    {
        if (string.IsNullOrEmpty(payload)) return "";
        try
        {
            var bytes = new DpapiSecretProtector().Unprotect(Convert.FromBase64String(payload));
            try { return Encoding.UTF8.GetString(bytes); } finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (CryptographicException) { return ""; }
        catch (FormatException) { return ""; }
    }
}
