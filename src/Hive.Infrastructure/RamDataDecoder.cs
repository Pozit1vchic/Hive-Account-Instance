using System.Security.Cryptography;
using System.Text;

namespace Hive.Infrastructure;

public static class RamDataDecoder
{
    private static readonly byte[] Entropy = Encoding.ASCII.GetBytes("ROBLOX ACCOUNT MANAGER | :) | BROUGHT TO YOU BUY ic3w0lf");
    private static readonly byte[] PasswordHeader = Encoding.ASCII.GetBytes("Roblox Account Manager created by ic3w0lf22 @ github.com ......");
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string Decode(byte[] data)
    {
        if (data.AsSpan().StartsWith(PasswordHeader)) throw new FormatException("This RAM file is password locked. Decrypt it in RAM before importing.");
        if (LooksLikeJson(data)) return StrictUtf8.GetString(data).TrimStart('\uFEFF');
        byte[] clear;
        try { clear = ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser); }
        catch (CryptographicException ex) { throw new FormatException("This RAM file could not be decrypted on this Windows account or computer.", ex); }
        try { return StrictUtf8.GetString(clear).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException ex) { throw new FormatException("The decrypted RAM file is not valid UTF-8.", ex); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    private static bool LooksLikeJson(byte[] data)
    {
        var span = data.AsSpan();
        if (span.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) span = span[3..];
        while (!span.IsEmpty && span[0] is 9 or 10 or 13 or 32) span = span[1..];
        return !span.IsEmpty && span[0] == (byte)'[';
    }
}
