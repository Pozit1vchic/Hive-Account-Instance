using System.Security.Cryptography;
using System.Text;

namespace Hive.Infrastructure;

public static class RamDataDecoder
{
    private static readonly byte[] Entropy = Encoding.ASCII.GetBytes("ROBLOX ACCOUNT MANAGER | :) | BROUGHT TO YOU BUY ic3w0lf");
    private static readonly byte[] PasswordHeader = Encoding.ASCII.GetBytes("Roblox Account Manager created by ic3w0lf22 @ github.com .......");
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static bool IsPasswordLocked(byte[] data) => data.AsSpan().StartsWith(PasswordHeader);
    public static string Decode(byte[] data, string? password = null)
    {
        if (IsPasswordLocked(data)) return DecodePasswordLocked(data, password);
        if (LooksLikeJson(data)) return StrictUtf8.GetString(data).TrimStart('\uFEFF');
        byte[] clear;
        try { clear = ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser); }
        catch (CryptographicException ex) { throw new FormatException("This RAM file could not be decrypted on this Windows account or computer.", ex); }
        try { return StrictUtf8.GetString(clear).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException ex) { throw new FormatException("The decrypted RAM file is not valid UTF-8.", ex); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    private static string DecodePasswordLocked(byte[] data, string? password)
    {
        if (string.IsNullOrEmpty(password)) throw new FormatException("Enter the RAM file password.");
        if (data.Length < PasswordHeader.Length + 16 + 24 + 16) throw new FormatException("The RAM file is incomplete.");
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var hash = SHA512.HashData(passwordBytes);
        byte[]? key = null;
        byte[]? clear = null;
        try
        {
            var offset = PasswordHeader.Length;
            var salt = data.AsSpan(offset, 16).ToArray(); offset += 16;
            var nonce = data.AsSpan(offset, 24).ToArray(); offset += 24;
            key = Sodium.PasswordHash.ArgonHashBinary(hash, salt, Sodium.PasswordHash.StrengthArgon.Moderate, 32);
            clear = Sodium.SecretBox.Open(data.AsSpan(offset).ToArray(), nonce, key);
            return StrictUtf8.GetString(clear).TrimStart('\uFEFF');
        }
        catch (Exception ex) when (ex is CryptographicException or DecoderFallbackException or ArgumentException)
        { throw new FormatException("The password is incorrect or the RAM file is damaged.", ex); }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes); CryptographicOperations.ZeroMemory(hash);
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            if (clear is not null) CryptographicOperations.ZeroMemory(clear);
        }
    }
    private static bool LooksLikeJson(byte[] data)
    {
        var span = data.AsSpan();
        if (span.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) span = span[3..];
        while (!span.IsEmpty && span[0] is 9 or 10 or 13 or 32) span = span[1..];
        return !span.IsEmpty && span[0] == (byte)'[';
    }
}
