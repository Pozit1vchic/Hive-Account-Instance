using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Hive.Core;
namespace Hive.Storage;

public sealed class VerifiedAccountStore(HiveDatabase database, ISecretProtector protector) : IVerifiedAccountStore
{
    private sealed record Existing(string Id, string DisplayName, string? GroupId, string Notes);
    public async Task<Account> SaveAsync(RobloxIdentity identity, AccountSession session, CancellationToken ct = default)
    {
        var clear = JsonSerializer.SerializeToUtf8Bytes(session);
        byte[] payload;
        try { payload = protector.Protect(clear); } finally { CryptographicOperations.ZeroMemory(clear); }
        await using var connection = await database.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var existing = await connection.QueryFirstOrDefaultAsync<Existing>(new CommandDefinition("SELECT Id,DisplayName,GroupId,Notes FROM Accounts WHERE RobloxUserId=@Id OR (RobloxUserId IS NULL AND Username=@Username COLLATE NOCASE) ORDER BY CASE WHEN RobloxUserId=@Id THEN 0 ELSE 1 END LIMIT 1", identity, transaction: tx, cancellationToken: ct));
        var account = new Account(existing?.Id ?? Guid.NewGuid().ToString("N"), identity.Username, existing?.DisplayName ?? identity.DisplayName, existing?.GroupId, existing?.Notes ?? "", SessionState.Valid, identity.Id);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO Accounts(Id,Username,DisplayName,GroupId,Notes,SessionState,RobloxUserId) VALUES(@Id,@Username,@DisplayName,@GroupId,@Notes,@SessionState,@RobloxUserId)
            ON CONFLICT(Id) DO UPDATE SET Username=excluded.Username,RobloxUserId=excluded.RobloxUserId,SessionState=excluded.SessionState;
            """, account, transaction: tx, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition("INSERT INTO Sessions(AccountId,Payload) VALUES(@id,@payload) ON CONFLICT(AccountId) DO UPDATE SET Payload=excluded.Payload", new { id = account.Id, payload }, transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return account;
    }
}
