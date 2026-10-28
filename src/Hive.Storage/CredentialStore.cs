using System.Text;
using Dapper;
using Hive.Core;
namespace Hive.Storage;

public sealed class CredentialStore(HiveDatabase database, ISecretProtector protector) : ICredentialStore
{
    public async Task SavePasswordAsync(string accountId, string password, CancellationToken ct = default)
    {
        var clear = Encoding.UTF8.GetBytes(password);
        try
        {
            var payload = protector.Protect(clear);
            await using var c = await database.OpenAsync(ct);
            await c.ExecuteAsync(new CommandDefinition("INSERT INTO Credentials(AccountId,Payload) VALUES(@accountId,@payload) ON CONFLICT(AccountId) DO UPDATE SET Payload=excluded.Payload", new
            {
                accountId,
                payload
            }, cancellationToken: ct));
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(clear); }
    }
    public async Task<string?> ReadPasswordAsync(string accountId, CancellationToken ct = default)
    {
        await using var c = await database.OpenAsync(ct);
        var bytes = await c.QuerySingleOrDefaultAsync<byte[]>(new CommandDefinition("SELECT Payload FROM Credentials WHERE AccountId=@accountId", new
        {
            accountId
        }, cancellationToken: ct));
        if (bytes is null)
            return null;
        var clear = protector.Unprotect(bytes);
        try
        {
            return Encoding.UTF8.GetString(clear);
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(clear); }
    }
    public async Task<bool> HasPasswordAsync(string accountId, CancellationToken ct = default)
    {
        await using var c = await database.OpenAsync(ct);
        return await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM Credentials WHERE AccountId=@accountId", new
        {
            accountId
        }, cancellationToken: ct)) > 0;
    }
}
