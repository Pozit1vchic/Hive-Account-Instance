using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Hive.Core;

namespace Hive.Storage;

public sealed class AccountFieldsStore(HiveDatabase database, ISecretProtector protector)
{
    public async Task<Dictionary<string, string>> ReadAsync(string accountId, CancellationToken ct = default)
    {
        await using var connection = await database.OpenAsync(ct);
        var payload = await connection.QuerySingleOrDefaultAsync<byte[]>(new CommandDefinition("SELECT Payload FROM AccountFields WHERE AccountId=@accountId", new { accountId }, cancellationToken: ct));
        if (payload is null) return new();
        var clear = protector.Unprotect(payload);
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(clear) ?? new(); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    public async Task SaveAsync(string accountId, IReadOnlyDictionary<string, string> fields, CancellationToken ct = default)
    {
        if (fields.Count > 100 || fields.Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Key.Length > 128 || x.Value is null || x.Value.Length > 5000)) throw new ArgumentException("Use up to 100 named fields, with names up to 128 and values up to 5,000 characters.");
        var clear = JsonSerializer.SerializeToUtf8Bytes(fields);
        byte[] payload;
        try { payload = protector.Protect(clear); } finally { CryptographicOperations.ZeroMemory(clear); }
        await using var connection = await database.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("INSERT INTO AccountFields(AccountId,Payload) VALUES(@accountId,@payload) ON CONFLICT(AccountId) DO UPDATE SET Payload=excluded.Payload", new { accountId, payload }, cancellationToken: ct));
    }
}
