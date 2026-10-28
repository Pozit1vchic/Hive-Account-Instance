using System.Text.Json;
using Dapper;
using Hive.Core;
namespace Hive.Storage;

public sealed class SessionStore(HiveDatabase database, ISecretProtector protector) : ISessionStore
{
    public async Task SaveAsync(string accountId, AccountSession session, CancellationToken cancellationToken = default)
    {
        var clear = JsonSerializer.SerializeToUtf8Bytes(session);
        byte[] payload;
        try
        {
            payload = protector.Protect(clear);
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(clear); }
        await using var c = await database.OpenAsync(cancellationToken);
        await c.ExecuteAsync(new CommandDefinition("INSERT INTO Sessions(AccountId,Payload) VALUES(@accountId,@payload) ON CONFLICT(AccountId) DO UPDATE SET Payload=excluded.Payload", new
        {
            accountId,
            payload
        }, cancellationToken: cancellationToken));
    }
    public async Task<AccountSession?> ReadAsync(string accountId, CancellationToken cancellationToken = default)
    {
        await using var c = await database.OpenAsync(cancellationToken);
        var payload = await c.QuerySingleOrDefaultAsync<byte[]>(new CommandDefinition("SELECT Payload FROM Sessions WHERE AccountId=@accountId", new
        {
            accountId
        }, cancellationToken: cancellationToken));
        if (payload is null)
            return null;
        var clear = protector.Unprotect(payload);
        try
        {
            return JsonSerializer.Deserialize<AccountSession>(clear);
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(clear); }
    }
}
